using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace P3Orbit
{
    // In-memory stand-in for a P3 CEM. It produces a plausible 254-byte config
    // block with a valid trailing sum so the whole read/edit/plan flow can be
    // exercised without a car. Every value is invented; see Profile p3.sim.
    internal sealed class Simulator
    {
        private readonly Catalog catalog;
        private byte[] block;
        private readonly uint seed;

        internal Simulator(Catalog catalog)
        {
            this.catalog = catalog;
            seed = Fnv("YV1FS8SYN0B100001");
            block = BuildBlock();
        }

        internal string Vin { get { return "YV1FS8SYN0B100001"; } }
        internal string Model { get { return "S60 II"; } }
        internal string Year { get { return "2013"; } }
        internal string Market { get { return "EU"; } }
        internal string Platform { get { return "P3"; } }

        internal List<Dictionary<string, object>> Modules()
        {
            return new List<Dictionary<string, object>>
            {
                Module("CEM", "Central electronic module", "0x726/0x72E", "P3SIM-CEM-0001", "P3SIM-CEM-SW-0001"),
                Module("ECM", "Engine control module", "0x7E0/0x7E8", "P3SIM-ECM-0001", "P3SIM-ECM-SW-0001"),
                Module("TCM", "Transmission control module", "0x7E1/0x7E9", "P3SIM-TCM-0001", "P3SIM-TCM-SW-0001"),
                Module("BCM", "Body control module", "0x740/0x748", "P3SIM-BCM-0001", "P3SIM-BCM-SW-0001"),
                Module("DDM", "Driver door module", "0x750/0x758", "P3SIM-DDM-0001", "P3SIM-DDM-SW-0001"),
                Module("SRS", "Supplemental restraint system", "0x770/0x778", "P3SIM-SRS-0001", "P3SIM-SRS-SW-0001")
            };
        }

        private static Dictionary<string, object> Module(string id, string name, string can, string hw, string sw)
        {
            return new Dictionary<string, object>
            {
                { "id", id }, { "name", name }, { "can", can },
                { "hardware", hw }, { "software", sw }, { "source", "simulator" }
            };
        }

        internal byte[] Read()
        {
            return (byte[])block.Clone();
        }

        internal void Write(byte[] next)
        {
            if (next == null || next.Length != block.Length)
                throw new InvalidOperationException("Simulated write must use a full " + block.Length + "-byte block.");
            block = (byte[])next.Clone();
        }

        private byte[] BuildBlock()
        {
            var rng = new Rng(seed);
            var bytes = new byte[catalog.configLength];

            // Header + a few non-parameter bytes drift deterministically so the
            // raw view looks like real data rather than zero fill.
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)rng.Next();

            foreach (var p in catalog.AllParams())
            {
                object value = p.@default;
                if (value == null) value = DefaultFor(p, rng);
                try { Codec.Write(bytes, p, value); }
                catch { /* a bad default must not stop the simulator; leave the byte as random */ }
            }

            if (catalog.checksum != null && catalog.checksum.enabled)
                bytes[catalog.checksum.byteIndex] = catalog.checksum.Compute(bytes);

            return bytes;
        }

        private static object DefaultFor(Param p, Rng rng)
        {
            switch ((p.type ?? "u8").ToLowerInvariant())
            {
                case "bool": return (rng.Next() & 1) == 1;
                case "enum": return p.values[(int)(rng.Next() % (uint)p.values.Count)].value;
                case "ascii": return "SIM" + rng.Next().ToString("X4");
                default:
                    double min = p.min;
                    double max = p.max > 0 ? p.max : (p.ByteLength > 1 ? 65535 : 255);
                    return Math.Round(min + (max - min) * ((rng.Next() % 100) / 100.0));
            }
        }

        private static uint Fnv(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text) { hash ^= c; hash *= 16777619; }
            return hash;
        }

        // xorshift32: tiny, deterministic, no allocation.
        private sealed class Rng
        {
            private uint state;
            internal Rng(uint seed) { state = seed == 0 ? 0x9E3779B9 : seed; }
            internal uint Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return state;
            }
        }
    }
}

