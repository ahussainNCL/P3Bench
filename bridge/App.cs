using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace P3Orbit
{
    internal sealed class ApiException : Exception
    {
        internal readonly string Code;
        internal readonly int Status;
        internal ApiException(string code, string message, int status = 409) : base(message)
        {
            Code = code;
            Status = status;
        }
    }

    internal sealed class LogEntry
    {
        public long seq;
        public string at;
        public string level;
        public string text;
    }

    internal sealed class Dump
    {
        public string id;
        public string source;
        public string provenance;
        public string capturedAt;
        public string vin;
        public byte[] bytes;

        internal Dictionary<string, object> ToJson(Catalog catalog)
        {
            var checksum = catalog.checksum;
            var result = new Dictionary<string, object>
            {
                { "id", id },
                { "source", source },
                { "provenance", provenance },
                { "capturedAt", capturedAt },
                { "vin", vin },
                { "length", bytes.Length },
                { "hex", Plan.Hex(bytes) },
                { "checksum", new Dictionary<string, object> { { "enabled", false } } }
            };

            if (checksum != null && checksum.enabled && checksum.to <= bytes.Length)
            {
                int expected = checksum.Compute(bytes);
                int actual = bytes[checksum.byteIndex];
                result["checksum"] = new Dictionary<string, object>
                {
                    { "enabled", true },
                    { "byteIndex", checksum.byteIndex },
                    { "from", checksum.from },
                    { "to", checksum.to },
                    { "expected", expected },
                    { "actual", actual },
                    { "ok", expected == actual }
                };
            }
            return result;
        }
    }

    internal sealed class App
    {
        private readonly object gate = new object();
        private readonly DateTime startedAt = DateTime.UtcNow;
        private readonly string root;
        private readonly int port;
        private readonly List<LogEntry> log = new List<LogEntry>();
        private readonly List<Adapter> adapters = new List<Adapter>();
        private long logSeq;

        private string mode = "simulator";
        private Adapter selectedAdapter;
        private J2534Device device;
        private J2534Channel channel;
        private double voltage = -1;
        private string connectionError;

        private Catalog catalog;
        private string catalogPath;
        private string catalogSource = "built-in";
        private List<Profile> profiles;
        private Profile profile;
        private Simulator simulator;
        private Dump dump;
        private Plan staged;

        internal App(string root, int port)
        {
            this.root = root;
            this.port = port;
            catalogPath = Path.Combine(root, "catalog", "p3.demo.json");
            ReloadCatalog();
            profiles = Profile.LoadAll(Path.Combine(root, "profiles.local.json"), Note);
            profile = profiles.Find(p => p.synthetic) ?? profiles[0];
            simulator = new Simulator(catalog);
            adapters.AddRange(AdapterDiscovery.Find());
            if (selectedAdapter == null && adapters.Count > 0) selectedAdapter = adapters[0];
            Log("info", "Bridge ready in simulator mode (" + adapters.Count + " J2534 driver(s) registered).");
        }

        internal void Shutdown()
        {
            lock (gate)
            {
                try { SafeClose(); }
                catch (Exception ex) { Console.WriteLine("Shutdown: " + ex.Message); }
            }
        }

        // ---------- plumbing ----------

        private void Log(string level, string text)
        {
            var entry = new LogEntry
            {
                seq = ++logSeq,
                at = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                level = level,
                text = text
            };
            log.Add(entry);
            if (log.Count > 800) log.RemoveRange(0, log.Count - 800);
            Console.WriteLine("[" + level + "] " + text);
        }

        private void Note(string text) { Log("info", text); }

        private void ReloadCatalog()
        {
            catalog = CatalogIo.Load(catalogPath);
            catalogSource = "catalog/p3.demo.json";
            Log("info", "Catalog '" + catalog.id + "' loaded (" + catalog.configLength + " bytes, " +
                CountParams() + " parameters, synthetic=" + catalog.synthetic + ").");
        }

        private int CountParams()
        {
            int count = 0;
            foreach (var p in catalog.AllParams()) count++;
            return count;
        }

        private static Dictionary<string, object> D(params object[] pairs)
        {
            var result = new Dictionary<string, object>(pairs.Length / 2);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                result[Convert.ToString(pairs[i], CultureInfo.InvariantCulture)] = pairs[i + 1];
            return result;
        }

        // ---------- state snapshot ----------

        internal Dictionary<string, object> State()
        {
            lock (gate)
            {
                var warnings = new List<string>();
                if (catalog.synthetic)
                    warnings.Add("The active catalog is synthetic: parameter names, offsets and values are invented for the demo.");
                if (mode == "simulator")
                    warnings.Add("Simulator mode: no adapter or vehicle is connected. Nothing here comes from a car.");
                if (mode == "hardware" && profile != null && !profile.validated)
                    warnings.Add("Active profile is not vehicle-validated. Config reads and writes are restricted.");
                if (dump != null && dump.checksumFailed(catalog))
                    warnings.Add("The loaded block fails its own checksum. It can be inspected, but changes cannot be staged until a valid block is loaded.");

                bool canRead = profile != null && profile.read != null &&
                    (profile.read.method == "simulator" || profile.read.method == "did");
                bool canApply = mode == "simulator";
                bool canUndo = mode == "simulator" && undoStack.Count > 0;
                string applyReason = canApply
                    ? "Simulator: applies to the in-memory block only."
                    : "Hardware writes are not implemented. The bridge refuses to write to a vehicle.";

                return D(
                    "ok", true,
                    "bridge", D(
                        "version", "0.1.0",
                        "port", port,
                        "pid", System.Diagnostics.Process.GetCurrentProcess().Id,
                        "startedAt", startedAt.ToString("o", CultureInfo.InvariantCulture),
                        "uptimeSec", (int)(DateTime.UtcNow - startedAt).TotalSeconds),
                    "mode", mode,
                    "adapters", AdapterList(),
                    "adapter", AdapterState(),
                    "profile", ProfileJson(profile, true),
                    "profiles", ProfileList(),
                    "vehicle", VehicleJson(),
                    "connection", D(
                        "state", channel != null ? "connected" : (device != null ? "adapter-open" : "disconnected"),
                        "error", connectionError),
                    "catalog", CatalogJson(),
                    "dump", dump == null ? null : dump.ToJson(catalog),
                    "staged", staged == null ? null : D(
                        "id", staged.id,
                        "summary", staged.summary,
                        "changedBytes", staged.changedBytes,
                        "blockHex", Plan.Hex(staged.Block),
                        "changes", ChangesJson()),
                    "warnings", warnings,
                    "capabilities", D(
                        "read", canRead,
                        "apply", canApply,
                        "applyReason", applyReason,
                        "undo", canUndo,
                        "undoDepth", undoStack.Count,
                        "reason", canRead ? null : (profile == null || profile.read == null ? "No read method." : profile.read.note)),
                    "catalogSource", catalogSource);
            }
        }

        private List<Dictionary<string, object>> ChangesJson()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var c in staged.changes)
                list.Add(D("paramId", c.paramId, "label", c.label, "offset", c.offset,
                    "oldHex", c.oldHex, "newHex", c.newHex,
                    "oldDisplay", c.oldDisplay, "newDisplay", c.newDisplay, "kind", c.kind));
            return list;
        }

        private List<Dictionary<string, object>> AdapterList()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var a in adapters)
                list.Add(D("name", a.Name, "vendor", a.Vendor, "library", a.Library,
                    "selected", selectedAdapter != null && selectedAdapter.Library == a.Library));
            return list;
        }

        private Dictionary<string, object> AdapterState()
        {
            if (device == null && selectedAdapter == null) return null;
            return D(
                "name", selectedAdapter == null ? null : selectedAdapter.Name,
                "vendor", selectedAdapter == null ? null : selectedAdapter.Vendor,
                "library", selectedAdapter == null ? null : selectedAdapter.Library,
                "open", device != null,
                "voltage", voltage < 0 ? (object)null : Math.Round(voltage, 2));
        }

        private List<Dictionary<string, object>> ProfileList()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var p in profiles) list.Add(ProfileJson(p, false));
            return list;
        }

        private Dictionary<string, object> ProfileJson(Profile p, bool active)
        {
            if (p == null) return null;
            return D(
                "id", p.id,
                "label", p.label,
                "description", p.description,
                "platform", p.platform,
                "synthetic", p.synthetic,
                "validated", p.validated,
                "configLength", p.configLength,
                "requestId", "0x" + p.Request.ToString("X3", CultureInfo.InvariantCulture),
                "responseId", "0x" + p.Response.ToString("X3", CultureInfo.InvariantCulture),
                "baud", p.Baud,
                "read", p.read == null ? null : D("method", p.read.method, "did", p.read.did, "length", p.read.length, "note", p.read.note),
                "notes", p.notes,
                "active", active);
        }

        private Dictionary<string, object> CatalogJson()
        {
            var groups = new List<Dictionary<string, object>>();
            foreach (var g in catalog.groups)
            {
                var parameters = new List<Dictionary<string, object>>();
                foreach (var p in g.@params)
                {
                    var item = D(
                        "id", p.id,
                        "label", p.label,
                        "description", p.description,
                        "type", p.type,
                        "offset", p.offset,
                        "bit", p.bit,
                        "length", p.ByteLength,
                        "min", p.min,
                        "max", p.max,
                        "scale", p.scale == 0 ? 1 : p.scale,
                        "unit", p.unit,
                        "notes", p.notes);
                    var values = new List<Dictionary<string, object>>();
                    if (p.values != null)
                        foreach (var v in p.values) values.Add(D("value", v.value, "label", v.label));
                    item["values"] = values;
                    if (dump != null && p.offset + p.ByteLength <= dump.bytes.Length)
                    {
                        try
                        {
                            item["value"] = Codec.Read(dump.bytes, p);
                            item["display"] = Codec.Display(dump.bytes, p);
                        }
                        catch (Exception ex) { item["display"] = "decode error: " + ex.Message; }
                    }
                    parameters.Add(item);
                }
                groups.Add(D("id", g.id, "label", g.label, "description", g.description, "params", parameters));
            }

            return D(
                "id", catalog.id,
                "label", catalog.label,
                "description", catalog.description,
                "platform", catalog.platform,
                "synthetic", catalog.synthetic,
                "configLength", catalog.configLength,
                "checksum", catalog.checksum == null ? null : D("enabled", catalog.checksum.enabled,
                    "byteIndex", catalog.checksum.byteIndex, "from", catalog.checksum.from, "to", catalog.checksum.to),
                "groups", groups);
        }

        private Dictionary<string, object> VehicleJson()
        {
            if (mode == "simulator" && simulator != null)
            {
                return D(
                    "vin", simulator.Vin,
                    "model", simulator.Model,
                    "year", simulator.Year,
                    "market", simulator.Market,
                    "platform", simulator.Platform,
                    "source", "simulator",
                    "modules", simulator.Modules());
            }
            if (channel != null && vehicleVin != null)
            {
                return D(
                    "vin", vehicleVin,
                    "model", null,
                    "year", null,
                    "market", null,
                    "platform", profile == null ? null : profile.platform,
                    "source", "vehicle (read-only probe)",
                    "modules", new List<object>());
            }
            return null;
        }

        private string vehicleVin;

        // ---------- adapter / connection ----------

        internal Dictionary<string, object> RefreshAdapters()
        {
            lock (gate)
            {
                RequireDisconnected("refresh the adapter list");
                adapters.Clear();
                adapters.AddRange(AdapterDiscovery.Find());
                Log("info", "Found " + adapters.Count + " J2534 driver(s).");
                if (selectedAdapter == null && adapters.Count > 0) selectedAdapter = adapters[0];
                return State();
            }
        }

        internal Dictionary<string, object> SelectAdapter(string library)
        {
            lock (gate)
            {
                RequireDisconnected("change the adapter");
                var match = adapters.Find(a => String.Equals(a.Library, library, StringComparison.OrdinalIgnoreCase));
                if (match == null) throw new ApiException("adapter_unknown", "That driver is not in the discovered list.", 404);
                selectedAdapter = match;
                Log("info", "Selected adapter '" + match.Name + "'.");
                return State();
            }
        }

        internal Dictionary<string, object> SetMode(string next)
        {
            lock (gate)
            {
                RequireDisconnected("change mode");
                if (next != "simulator" && next != "hardware")
                    throw new ApiException("bad_mode", "Mode must be 'simulator' or 'hardware'.");
                mode = next;
                dump = null;
                staged = null;
                vehicleVin = null;
                if (next == "simulator")
                {
                    profile = profiles.Find(p => p.synthetic) ?? profiles[0];
                    if (simulator == null) simulator = new Simulator(catalog);
                }
                else
                {
                    profile = profiles.Find(p => !p.synthetic) ?? profiles[0];
                }
                Log("info", "Mode set to " + next + " using profile '" + profile.id + "'.");
                return State();
            }
        }

        internal Dictionary<string, object> Connect()
        {
            lock (gate)
            {
                if (mode == "simulator") throw new ApiException("sim_connected", "Simulator mode has no adapter to open. Switch to hardware mode.");
                if (selectedAdapter == null) throw new ApiException("no_adapter", "Select an adapter first.");

                try
                {
                    device = new J2534Device();
                    device.Open(selectedAdapter);
                    Log("info", "Opened " + selectedAdapter.Name + ".");
                    try
                    {
                        voltage = device.ReadVoltage();
                        Log("info", "Supply voltage " + voltage.ToString("0.00", CultureInfo.InvariantCulture) + " V.");
                    }
                    catch (Exception ex) { voltage = -1; Log("warn", "Voltage read failed: " + ex.Message); }

                    channel = new J2534Channel(device, profile.Request, profile.Response, profile.Baud);
                    Log("info", "ISO 15765 channel open: 0x" + profile.Request.ToString("X3") +
                        " -> 0x" + profile.Response.ToString("X3") + " @ " + profile.Baud + " bit/s.");

                    ProbeTesterPresent();

                    try { vehicleVin = ReadVin(); }
                    catch (Exception ex)
                    {
                        vehicleVin = null;
                        Log("warn", "VIN read failed: " + ex.Message);
                    }
                    connectionError = null;
                    Log("info", "VIN read: " + (vehicleVin ?? "(no response)") + ".");
                }
                catch (Exception ex)
                {
                    connectionError = ex.Message;
                    Log("error", "Connect failed: " + ex.Message);
                    SafeClose();
                    device = null;
                    throw new ApiException("connect_failed", ex.Message, 502);
                }
                return State();
            }
        }

        internal Dictionary<string, object> Disconnect()
        {
            lock (gate)
            {
                SafeClose();
                vehicleVin = null;
                connectionError = null;
                Log("info", "Disconnected.");
                return State();
            }
        }

        private void SafeClose()
        {
            try { if (channel != null) channel.Dispose(); }
            catch (Exception ex) { Log("warn", "Channel close: " + ex.Message); }
            channel = null;
            try { if (device != null) device.Dispose(); }
            catch (Exception ex) { Log("warn", "Device close: " + ex.Message); }
            device = null;
        }

        private void RequireDisconnected(string action)
        {
            if (device != null || channel != null)
                throw new ApiException("busy", "Disconnect before you " + action + ".");
        }

        private void ProbeTesterPresent()
        {
            try
            {
                byte[] response = channel.Request(new byte[] { 0x3E, 0x00 }, 300);
                if (response != null && response.Length > 0 && response[0] == 0x7E)
                    Log("info", "Tester present acknowledged (3E 00 -> 7E 00).");
                else if (response != null && response.Length > 0 && response[0] == 0x7F)
                    Log("warn", "CEM rejected 3E 00: NRC 0x" + response[2].ToString("X2") + ".");
            }
            catch (Exception ex)
            {
                // A silent module is a finding, not a reason to drop the channel.
                Log("warn", "3E 00 probe got no usable answer: " + ex.Message);
            }
        }

        private string ReadVin()
        {
            byte[] data = ReadDid(0xF190, 2000);
            if (data == null) return null;
            var sb = new StringBuilder();
            foreach (byte b in data)
            {
                if (b == 0 || b == 0x55 || b == 0xAA) break;
                if (b >= 0x20 && b < 0x7F) sb.Append((char)b);
            }
            string vin = sb.ToString().Trim();
            return vin.Length == 0 ? null : vin;
        }

        private byte[] ReadDid(ushort did, int timeoutMs)
        {
            if (channel == null) throw new ApiException("not_connected", "Connect to the vehicle first.");
            byte[] request = new byte[] { 0x22, (byte)(did >> 8), (byte)did };
            byte[] response = channel.Request(request, timeoutMs);
            if (response == null || response.Length == 0)
                throw new ApiException("empty_response", "No response to 22 " + did.ToString("X4") + ".", 502);
            if (response[0] == 0x7F)
            {
                string nrc = response.Length > 2 ? " NRC 0x" + response[2].ToString("X2") : "";
                throw new ApiException("negative_response", "22 " + did.ToString("X4") + " was rejected." + nrc, 502);
            }
            string expected = "62" + did.ToString("X4");
            if (response.Length < 3 || response[0] != 0x62 || response[1] != (byte)(did >> 8) || response[2] != (byte)did)
                throw new ApiException("bad_response", "Unexpected reply to 22 " + did.ToString("X4") + " (" + Plan.Hex(response) + ").", 502);
            var data = new byte[response.Length - 3];
            Buffer.BlockCopy(response, 3, data, 0, data.Length);
            return data;
        }

        // ---------- config read / import ----------

        internal Dictionary<string, object> Read()
        {
            lock (gate)
            {
                if (profile == null || profile.read == null) throw new ApiException("no_profile", "No profile is active.");
                byte[] bytes;
                if (profile.read.method == "simulator")
                {
                    if (simulator == null) simulator = new Simulator(catalog);
                    bytes = simulator.Read();
                }
                else if (profile.read.method == "did")
                {
                    if (channel == null) throw new ApiException("not_connected", "Connect to the vehicle first.");
                    uint rawDid = Profile.ParseHex(profile.read.did, 0xFFFF);
                    if (rawDid == 0xFFFF) throw new ApiException("bad_profile", "Profile read.did is not a valid DID.");
                    byte[] data = ReadDid((ushort)rawDid, 5000);
                    // The profile declares how many bytes the DID returns; the
                    // catalog decides how many of them can be decoded. Require
                    // both, then keep exactly the catalog's block length.
                    int want = catalog.configLength;
                    int declared = profile.read.length > 0
                        ? profile.read.length
                        : (profile.configLength > 0 ? profile.configLength : want);
                    int minimum = Math.Max(want, declared);
                    if (data.Length < minimum)
                        throw new ApiException("short_read", "DID returned " + data.Length + " bytes, expected at least " + minimum + ".", 502);
                    bytes = new byte[want];
                    Buffer.BlockCopy(data, 0, bytes, 0, want);
                    Log("info", "Config read via 22 " + rawDid.ToString("X4") + ": " + bytes.Length + " bytes (DID returned " + data.Length + ").");
                }
                else
                {
                    throw new ApiException("no_read_path", profile.read.note ?? "No verified config read method for this profile.");
                }

                dump = new Dump
                {
                    id = "dump_" + DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture),
                    source = profile.read.method == "simulator" ? "simulator" : ("vehicle 22 " + profile.read.did),
                    provenance = profile.read.method == "simulator"
                        ? "Synthetic block generated by the bridge. Not vehicle data."
                        : "Read-only UDS response, profile '" + profile.id + "'.",
                    capturedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    vin = mode == "simulator" ? simulator.Vin : vehicleVin,
                    bytes = bytes
                };
                staged = null;

                var json = dump.ToJson(catalog);
                var checksum = json["checksum"] as Dictionary<string, object>;
                if (checksum != null && checksum.ContainsKey("ok") && !(bool)checksum["ok"])
                    Log("warn", "Block checksum does not match (stored " + checksum["actual"] + ", computed " + checksum["expected"] + ").");

                Log("info", "Config read: " + bytes.Length + " bytes, checksum " +
                    (checksum != null && checksum.ContainsKey("ok") ? ((bool)checksum["ok"] ? "ok" : "MISMATCH") : "not checked") + ".");
                return State();
            }
        }

        internal Dictionary<string, object> ImportDump(string hex)
        {
            lock (gate)
            {
                byte[] bytes = ParseHexBytes(hex);
                if (bytes.Length != catalog.configLength)
                    throw new ApiException("bad_length",
                        "Imported " + bytes.Length + " bytes; this catalog expects " + catalog.configLength + ".",
                        422);
                dump = new Dump
                {
                    id = "dump_" + DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture),
                    source = "imported",
                    provenance = "Hex text pasted into the web UI. Not read from a vehicle by this bridge.",
                    capturedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    vin = null,
                    bytes = bytes
                };
                staged = null;
                Log("info", "Imported a " + bytes.Length + "-byte block from text.");
                return State();
            }
        }

        private static byte[] ParseHexBytes(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new ApiException("empty", "No hex data supplied.", 422);
            string trimmed = text.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(2);

            var cleaned = new StringBuilder(trimmed.Length);
            foreach (char c in trimmed)
            {
                if (Uri.IsHexDigit(c)) cleaned.Append(c);
                else if (c == ' ' || c == '\r' || c == '\n' || c == '\t' || c == ',' || c == ':' || c == '-')
                    continue;
                else throw new ApiException("bad_hex", "Unexpected character in hex data: '" + c + "'.", 422);
            }
            string s = cleaned.ToString();
            if (s.Length % 2 != 0) throw new ApiException("bad_hex", "Hex data has an odd number of digits.", 422);
            var bytes = new byte[s.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(s.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        // ---------- staging ----------

        internal Dictionary<string, object> PlanEdits(List<Edit> edits)
        {
            lock (gate)
            {
                if (dump == null) throw new ApiException("no_dump", "Read or import a config block first.");
                staged = Plan.Create(catalog, dump.bytes, edits, () => "plan_" + DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture));
                Log("info", "Staged " + staged.summary + " (not applied).");
                return State();
            }
        }

        internal Dictionary<string, object> Discard()
        {
            lock (gate)
            {
                if (staged != null) Log("info", "Discarded staged plan " + staged.id + ".");
                staged = null;
                return State();
            }
        }

        internal Dictionary<string, object> Apply(string planId, string confirm)
        {
            lock (gate)
            {
                if (staged == null) throw new ApiException("no_plan", "Nothing is staged.");
                if (!String.Equals(staged.id, planId, StringComparison.Ordinal))
                    throw new ApiException("stale_plan", "That plan id is not the one staged.");
                if (!String.Equals((confirm ?? "").Trim(), "APPLY", StringComparison.Ordinal))
                    throw new ApiException("confirm_required", "Type APPLY to confirm the write.");

                if (mode != "simulator")
                    throw new ApiException("writes_disabled",
                        "Hardware writes are deliberately not implemented. The bridge will not write to a vehicle.", 403);
                if (catalog.checksum != null && catalog.checksum.enabled)
                {
                    int expected = catalog.checksum.Compute(staged.Block);
                    if (staged.Block[catalog.checksum.byteIndex] != expected)
                        throw new ApiException("bad_checksum", "The staged block fails its own checksum; refusing to apply.");
                }

                Log("info", "SIMULATED write: 10 82, 27 01/27 02, 2E " + catalog.id + ", 31 01 checksum.");
                simulator.Write(staged.Block);
                string applied = staged.summary;
                staged = null;
                dump = new Dump
                {
                    id = "dump_" + DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture),
                    source = "simulator (after write)",
                    provenance = "Synthetic block after a simulated write. Not vehicle data.",
                    capturedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    vin = simulator.Vin,
                    bytes = simulator.Read()
                };
                Log("info", "Applied in simulator: " + applied + ".");
                return State();
            }
        }

        // ---------- catalog / logs ----------

        internal Dictionary<string, object> ReloadCatalogFromDisk()
        {
            lock (gate)
            {
                ReloadCatalog();
                dump = null;
                staged = null;
                simulator = new Simulator(catalog);
                Log("info", "Catalog reloaded; read/import the config again.");
                return State();
            }
        }

        internal Dictionary<string, object> ImportCatalog(string text)
        {
            lock (gate)
            {
                if (String.IsNullOrWhiteSpace(text)) throw new ApiException("empty", "No catalog JSON supplied.", 422);
                Catalog candidate;
                try { candidate = CatalogIo.Parse(text); }
                catch (Exception ex) { throw new ApiException("bad_catalog", ex.Message, 422); }
                if (candidate.configLength != catalog.configLength && dump != null)
                {
                    dump = null;
                    staged = null;
                    Log("info", "Block length changed; the previous read was dropped.");
                }
                catalog = candidate;
                catalogSource = "imported via web UI";
                if (mode == "simulator") simulator = new Simulator(catalog);
                Log("info", "Catalog '" + catalog.id + "' imported (" + CountParams() + " parameters).");
                return State();
            }
        }

        internal Dictionary<string, object> Logs(long since)
        {
            lock (gate)
            {
                var entries = new List<Dictionary<string, object>>();
                foreach (var entry in log)
                    if (entry.seq > since)
                        entries.Add(D("seq", entry.seq, "at", entry.at, "level", entry.level, "text", entry.text));
                return D("ok", true, "seq", logSeq, "entries", entries);
            }
        }
    }
}

