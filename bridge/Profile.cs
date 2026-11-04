using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace P3Orbit
{
    internal sealed class ProfileRead
    {
        // "simulator" - synthetic ECU, "did" - UDS 22 read of a verified DID,
        // "none" - no config read path is known/verified for this profile.
        public string method { get; set; }
        public string did { get; set; }
        public int length { get; set; }
        public string note { get; set; }
    }

    // A profile says how to talk to one P3 build: CAN IDs, config length and
    // whether the read path has ever been verified on a car. "validated" is
    // deliberately conservative: anything not proven stays false and the bridge
    // refuses writes for it.
    internal sealed class Profile
    {
        public string id { get; set; }
        public string label { get; set; }
        public string description { get; set; }
        public string platform { get; set; }
        public bool synthetic { get; set; }
        public bool validated { get; set; }
        public string requestId { get; set; }
        public string responseId { get; set; }
        public int baud { get; set; }
        public int configLength { get; set; }
        public ProfileRead read { get; set; }
        public List<string> notes { get; set; }

        public uint Request { get { return ParseHex(requestId, 0x726); } }
        public uint Response { get { return ParseHex(responseId, 0x72E); } }
        public uint Baud { get { return (uint)(baud > 0 ? baud : 500000); } }

        internal static uint ParseHex(string text, uint fallback)
        {
            if (String.IsNullOrWhiteSpace(text)) return fallback;
            string s = text.Trim();
            uint value;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return UInt32.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) ? value : fallback;
            return UInt32.TryParse(s, out value) ? value : fallback;
        }

        internal static List<Profile> Defaults()
        {
            var list = new List<Profile>();
            list.Add(new Profile
            {
                id = "p3.sim",
                label = "P3 simulator",
                description = "Synthetic P3 CEM used for UI work. No adapter, no vehicle.",
                platform = "P3",
                synthetic = true,
                validated = false,
                configLength = 254,
                read = new ProfileRead { method = "simulator", length = 254, note = "Generated in-process. Values are invented." },
                notes = new List<string> {
                    "Everything here is synthetic: VIN, part numbers, module list and config bytes.",
                    "Writes only ever touch the in-memory simulator block."
                }
            });
            list.Add(new Profile
            {
                id = "p3.cem-726",
                label = "P3 CEM (0x726 / 0x72E, 500k)",
                description = "Read-only identity probes. No verified config read or write path.",
                platform = "P3",
                synthetic = false,
                validated = false,
                requestId = "0x726",
                responseId = "0x72E",
                baud = 500000,
                configLength = 254,
                read = new ProfileRead
                {
                    method = "none",
                    length = 254,
                    note = "No verified P3 CarConfig read method is implemented. Add a verified DID in profiles.local.json."
                },
                notes = new List<string> {
                    "Addressing and baud follow the P3ToolClone investigation for 0x726/0x72E. Not vehicle-validated.",
                    "Only 3E 00 and 22 F1 90-style reads are sent.",
                    "Writes are refused for every non-synthetic profile."
                }
            });
            return list;
        }

        internal static List<Profile> LoadAll(string localPath, Action<string> log)
        {
            List<Profile> all = Defaults();
            if (!File.Exists(localPath)) return all;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
                string text = File.ReadAllText(localPath, Encoding.UTF8);
                var extra = serializer.Deserialize<List<Profile>>(text);
                foreach (var p in extra ?? new List<Profile>())
                {
                    if (String.IsNullOrWhiteSpace(p.id)) continue;
                    p.platform = p.platform ?? "P3";
                    p.read = p.read ?? new ProfileRead { method = "none", length = p.configLength };
                    all.RemoveAll(x => x.id == p.id);
                    all.Add(p);
                    log("Loaded local profile '" + p.id + "' (validated=" + p.validated + ").");
                }
            }
            catch (Exception ex)
            {
                log("profiles.local.json was ignored: " + ex.Message);
            }
            return all;
        }
    }
}
