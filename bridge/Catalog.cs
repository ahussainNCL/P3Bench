using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace P3Orbit
{
    internal sealed class Catalog
    {
        public string id { get; set; }
        public string label { get; set; }
        public string description { get; set; }
        public bool synthetic { get; set; }
        public string platform { get; set; }
        public int configLength { get; set; }
        public Checksum checksum { get; set; }
        public List<Group> groups { get; set; }

        public IEnumerable<Param> AllParams()
        {
            if (groups == null) yield break;
            foreach (var g in groups)
            {
                if (g.@params == null) continue;
                foreach (var p in g.@params) yield return p;
            }
        }

        public Param Find(string id)
        {
            foreach (var p in AllParams())
                if (String.Equals(p.id, id, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }
    }

    internal sealed class Group
    {
        public string id { get; set; }
        public string label { get; set; }
        public string description { get; set; }
        public List<Param> @params { get; set; }
    }

    internal sealed class Param
    {
        public string id { get; set; }
        public string label { get; set; }
        public string description { get; set; }
        public string type { get; set; }
        public int offset { get; set; }
        public int bit { get; set; }
        public int length { get; set; }
        public double min { get; set; }
        public double max { get; set; }
        public double scale { get; set; }
        public string unit { get; set; }
        public List<EnumValue> values { get; set; }
        public string notes { get; set; }
        public object @default { get; set; }

        public int ByteLength
        {
            get
            {
                switch ((type ?? "u8").ToLowerInvariant())
                {
                    case "bool": return 1;
                    case "enum": return 1;
                    case "u8": return 1;
                    case "u16le":
                    case "u16be": return 2;
                    case "ascii": return length > 0 ? length : 1;
                    default: return 1;
                }
            }
        }
    }

    internal sealed class EnumValue
    {
        public int value { get; set; }
        public string label { get; set; }
    }

    internal sealed class Checksum
    {
        public bool enabled { get; set; }
        public int byteIndex { get; set; }
        public int from { get; set; }
        public int to { get; set; }

        public static Checksum Disabled()
        {
            return new Checksum { enabled = false };
        }

        public byte Compute(byte[] block)
        {
            int sum = 0;
            for (int i = from; i < to && i < block.Length; i++) sum = (sum + block[i]) & 0xFF;
            return (byte)sum;
        }
    }

    internal static class CatalogIo
    {
        private static readonly HashSet<string> KnownTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bool", "enum", "u8", "u16le", "u16be", "ascii"
        };

        internal static Catalog Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Catalog file not found.", path);
            string text = File.ReadAllText(path, Encoding.UTF8);
            Catalog catalog = Parse(text);
            return catalog;
        }

        internal static Catalog Parse(string text)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            Catalog catalog = serializer.Deserialize<Catalog>(text);
            Validate(catalog);
            return catalog;
        }

        internal static void Validate(Catalog catalog)
        {
            if (catalog == null) throw new InvalidDataException("Catalog is not a JSON object.");
            if (String.IsNullOrWhiteSpace(catalog.id)) throw new InvalidDataException("Catalog has no id.");
            if (catalog.groups == null || catalog.groups.Count == 0)
                throw new InvalidDataException("Catalog has no groups.");
            if (catalog.configLength <= 0 || catalog.configLength > 4096)
                throw new InvalidDataException("Catalog configLength must be 1-4096.");

            foreach (var group in catalog.groups)
            {
                if (group == null) throw new InvalidDataException("Catalog contains a null group.");
                if (group.@params == null) continue;
                foreach (var parameter in group.@params)
                    if (parameter == null) throw new InvalidDataException("Group '" + group.id + "' contains a null parameter.");
            }

            var wholeByte = new Dictionary<int, string>();
            var bitUse = new Dictionary<int, string>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in catalog.AllParams())
            {
                if (String.IsNullOrWhiteSpace(p.id)) throw new InvalidDataException("A parameter has no id.");
                if (!ids.Add(p.id)) throw new InvalidDataException("Duplicate parameter id: " + p.id);
                if (p.offset < 0 || p.offset + p.ByteLength > catalog.configLength)
                    throw new InvalidDataException(p.id + ": offset/length falls outside the config block.");

                string type = (p.type ?? "u8").ToLowerInvariant();
                if (!KnownTypes.Contains(type))
                    throw new InvalidDataException(p.id + ": unsupported parameter type '" + p.type + "'.");
                if (type == "ascii" && p.length < 1)
                    throw new InvalidDataException(p.id + ": ascii length must be at least 1.");
                if (p.scale < 0)
                    throw new InvalidDataException(p.id + ": scale must not be negative.");

                double scale = p.scale == 0 ? 1 : p.scale;
                if (p.min < 0)
                    throw new InvalidDataException(p.id + ": min must not be negative.");
                if (p.max > 0 && p.max < p.min)
                    throw new InvalidDataException(p.id + ": max must be greater than min.");

                if (type == "u8" || type == "u16le" || type == "u16be")
                {
                    double rawMax = type == "u8" ? 255 : 65535;
                    if (p.min / scale > rawMax)
                        throw new InvalidDataException(p.id + ": min cannot be stored at this scale.");
                    if (p.max > 0 && p.max / scale > rawMax)
                        throw new InvalidDataException(p.id + ": max cannot be stored at this scale.");
                }

                bool isBit = type == "bool";
                if (isBit)
                {
                    if (p.bit < 0 || p.bit > 7) throw new InvalidDataException(p.id + ": bool bit must be 0-7.");
                    string other;
                    if (wholeByte.TryGetValue(p.offset, out other))
                        throw new InvalidDataException("Parameters " + other + " and " + p.id + " overlap at byte " + p.offset + ".");
                    int key = p.offset * 8 + p.bit;
                    if (bitUse.TryGetValue(key, out other))
                        throw new InvalidDataException("Parameters " + other + " and " + p.id + " use the same bit at byte " + p.offset + ".");
                    bitUse[key] = p.id;
                }
                else
                {
                    for (int i = 0; i < p.ByteLength; i++)
                    {
                        int at = p.offset + i;
                        string other;
                        if (wholeByte.TryGetValue(at, out other))
                            throw new InvalidDataException("Parameters " + other + " and " + p.id + " overlap at byte " + at + ".");
                        for (int b = 0; b < 8; b++)
                            if (bitUse.TryGetValue(at * 8 + b, out other))
                                throw new InvalidDataException("Parameters " + other + " and " + p.id + " overlap at byte " + at + ".");
                        wholeByte[at] = p.id;
                    }
                }

                if (type == "enum")
                {
                    if (p.values == null || p.values.Count == 0)
                        throw new InvalidDataException(p.id + ": enum without values.");
                    var seen = new HashSet<int>();
                    foreach (var value in p.values)
                    {
                        if (value == null) throw new InvalidDataException(p.id + ": enum contains a null value.");
                        if (value.value < 0 || value.value > 255)
                            throw new InvalidDataException(p.id + ": enum value " + value.value + " does not fit in one byte.");
                        if (!seen.Add(value.value))
                            throw new InvalidDataException(p.id + ": duplicate enum value " + value.value + ".");
                        if (String.IsNullOrWhiteSpace(value.label))
                            throw new InvalidDataException(p.id + ": enum value " + value.value + " has no label.");
                    }
                }
            }

            if (catalog.checksum != null && catalog.checksum.enabled)
            {
                var c = catalog.checksum;
                if (c.from < 0 || c.to > catalog.configLength || c.from >= c.to)
                    throw new InvalidDataException("Checksum range is invalid for this block length.");
                if (c.byteIndex < 0 || c.byteIndex >= catalog.configLength)
                    throw new InvalidDataException("Checksum byte index is outside the block.");
                if (c.byteIndex >= c.from && c.byteIndex < c.to)
                    throw new InvalidDataException("Checksum byte must sit outside the summed range.");
                string clash;
                if (wholeByte.TryGetValue(c.byteIndex, out clash))
                    throw new InvalidDataException("Checksum byte is also used by parameter " + clash + ".");
                for (int b = 0; b < 8; b++)
                    if (bitUse.TryGetValue(c.byteIndex * 8 + b, out clash))
                        throw new InvalidDataException("Checksum byte is also used by parameter " + clash + ".");
            }
        }
    }

    // Byte-level codec for one catalog entry. All bounds are already validated at load.
    internal static class Codec
    {
        internal static object Read(byte[] block, Param p)
        {
            string type = (p.type ?? "u8").ToLowerInvariant();
            switch (type)
            {
                case "bool":
                    return ((block[p.offset] >> p.bit) & 1) != 0;
                case "enum":
                case "u8":
                    return (int)block[p.offset];
                case "u16le":
                    return (int)block[p.offset] | (block[p.offset + 1] << 8);
                case "u16be":
                    return (block[p.offset] << 8) | (int)block[p.offset + 1];
                case "ascii":
                    int len = p.ByteLength;
                    return Encoding.ASCII.GetString(block, p.offset, len).TrimEnd('\0');
                default:
                    return (int)block[p.offset];
            }
        }

        internal static double Scaled(byte[] block, Param p)
        {
            object raw = Read(block, p);
            double factor = p.scale == 0 ? 1 : p.scale;
            return Convert.ToDouble(raw, CultureInfo.InvariantCulture) * factor;
        }

        internal static string Display(byte[] block, Param p)
        {
            string type = (p.type ?? "u8").ToLowerInvariant();
            if (type == "bool") return ((bool)Read(block, p)) ? "On" : "Off";
            if (type == "enum")
            {
                int v = (int)Read(block, p);
                foreach (var e in p.values) if (e.value == v) return e.label;
                return "Unknown (" + v + ")";
            }
            if (type == "ascii") return Convert.ToString(Read(block, p));
            double scaled = Scaled(block, p);
            string number = scaled == Math.Floor(scaled)
                ? scaled.ToString("0", CultureInfo.InvariantCulture)
                : scaled.ToString("0.##", CultureInfo.InvariantCulture);
            return p.unit == null ? number : number + " " + p.unit;
        }

        // Writes value into a copy of block. Throws InvalidDataException with a
        // human-readable reason when the value is not representable.
        internal static void Write(byte[] block, Param p, object value)
        {
            string type = (p.type ?? "u8").ToLowerInvariant();
            int offset = p.offset;

            switch (type)
            {
                case "bool":
                {
                    bool on = ToBool(value);
                    block[offset] = (byte)(on ? (block[offset] | (1 << p.bit)) : (block[offset] & ~(1 << p.bit)));
                    return;
                }
                case "enum":
                {
                    int v = ToInt(value);
                    bool known = false;
                    foreach (var e in p.values) if (e.value == v) known = true;
                    if (!known) throw new InvalidDataException(p.label + ": " + v + " is not a listed value.");
                    block[offset] = (byte)v;
                    return;
                }
                case "u8":
                {
                    double scaled = ToDouble(value);
                    CheckRange(p, scaled);
                    block[offset] = (byte)ToRaw(p, scaled, 255);
                    return;
                }
                case "u16le":
                {
                    double scaled = ToDouble(value);
                    CheckRange(p, scaled);
                    int v = ToRaw(p, scaled, 65535);
                    block[offset] = (byte)(v & 0xFF);
                    block[offset + 1] = (byte)((v >> 8) & 0xFF);
                    return;
                }
                case "u16be":
                {
                    double scaled = ToDouble(value);
                    CheckRange(p, scaled);
                    int v = ToRaw(p, scaled, 65535);
                    block[offset] = (byte)((v >> 8) & 0xFF);
                    block[offset + 1] = (byte)(v & 0xFF);
                    return;
                }
                case "ascii":
                {
                    string text = Convert.ToString(value) ?? "";
                    int len = p.ByteLength;
                    if (text.Length > len) throw new InvalidDataException(p.label + ": at most " + len + " characters.");
                    for (int i = 0; i < len; i++)
                        block[offset + i] = (byte)(i < text.Length ? text[i] : 0);
                    return;
                }
                default:
                    throw new InvalidDataException("Unsupported parameter type: " + p.type);
            }
        }

        internal static bool Fits(Param p, object value)
        {
            try
            {
                byte[] copy = new byte[p.ByteLength + p.offset + 8];
                Write(copy, p, value);
                return true;
            }
            catch { return false; }
        }

        private static void CheckRange(Param p, double scaled)
        {
            double min = p.min;
            double max = p.max > 0 ? p.max : (p.ByteLength > 1 ? 65535 : 255);
            if (scaled < min || scaled > max)
                throw new InvalidDataException(p.label + ": " + scaled + " is outside " + min + ".." + max + ".");
        }

        // Converts a scaled display value to its raw unsigned integer and makes
        // sure it still fits in the parameter's storage width. Without this a
        // scale below 1 (or an over-wide max) would silently truncate the byte.
        private static int ToRaw(Param p, double scaled, int max)
        {
            double raw = scaled / (p.scale == 0 ? 1 : p.scale);
            double rounded = Math.Round(raw);
            if (rounded < 0 || rounded > max)
                throw new InvalidDataException(p.label + ": " + scaled.ToString(CultureInfo.InvariantCulture) +
                    " cannot be encoded (raw value " + rounded.ToString(CultureInfo.InvariantCulture) + " is outside 0.." + max + ").");
            return (int)rounded;
        }

        private static bool ToBool(object value)
        {
            if (value is bool) return (bool)value;
            string s = Convert.ToString(value);
            if (String.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (String.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            if (s == "1") return true;
            if (s == "0") return false;
            throw new InvalidDataException("Expected a boolean, got '" + s + "'.");
        }

        private static int ToInt(object value)
        {
            return (int)Math.Round(ToDouble(value));
        }

        private static double ToDouble(object value)
        {
            if (value == null) throw new InvalidDataException("Missing value.");
            if (value is int || value is long || value is double || value is decimal) return Convert.ToDouble(value);
            if (value is bool) return (bool)value ? 1 : 0;
            double parsed;
            if (Double.TryParse(Convert.ToString(value), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            throw new InvalidDataException("Expected a number, got '" + value + "'.");
        }
    }
}

