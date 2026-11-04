using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace P3Orbit
{
    internal sealed class Edit
    {
        public string id { get; set; }
        public object value { get; set; }
    }

    internal sealed class Change
    {
        public string paramId { get; set; }
        public string label { get; set; }
        public int offset { get; set; }
        public string oldHex { get; set; }
        public string newHex { get; set; }
        public string oldDisplay { get; set; }
        public string newDisplay { get; set; }
        public string kind { get; set; } // "param" | "checksum"
    }

    internal sealed class Plan
    {
        public string id { get; set; }
        public List<Change> changes { get; set; }
        public int changedBytes { get; set; }
        public string summary { get; set; }
        internal byte[] Block { get; set; }

        // Builds a candidate block from one base block plus edits. Nothing is
        // sent anywhere: the result is only a proposal until Apply is called.
        internal static Plan Create(Catalog catalog, byte[] baseBlock, List<Edit> edits, Func<string> newId)
        {
            if (baseBlock == null) throw new InvalidOperationException("Read the config before staging changes.");
            if (baseBlock.Length != catalog.configLength)
                throw new InvalidDataException("Base block is " + baseBlock.Length + " bytes, catalog expects " + catalog.configLength + ".");
            if (edits == null || edits.Count == 0) throw new InvalidDataException("No edits were supplied.");

            byte[] work = (byte[])baseBlock.Clone();
            var changes = new List<Change>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var edit in edits)
            {
                if (String.IsNullOrWhiteSpace(edit.id)) throw new InvalidDataException("An edit has no parameter id.");
                if (!seen.Add(edit.id)) throw new InvalidDataException("Parameter '" + edit.id + "' was edited twice in one plan.");
                Param p = catalog.Find(edit.id);
                if (p == null) throw new InvalidDataException("Unknown parameter '" + edit.id + "'.");

                int len = p.ByteLength;
                byte[] before = new byte[len];
                Buffer.BlockCopy(work, p.offset, before, 0, len);
                Codec.Write(work, p, edit.value);
                byte[] after = new byte[len];
                Buffer.BlockCopy(work, p.offset, after, 0, len);

                if (!Same(before, after))
                {
                    changes.Add(new Change
                    {
                        paramId = p.id,
                        label = p.label ?? p.id,
                        offset = p.offset,
                        oldHex = Hex(before),
                        newHex = Hex(after),
                        oldDisplay = Codec.Display(baseBlock, p),
                        newDisplay = Codec.Display(work, p),
                        kind = "param"
                    });
                }
            }

            var checksum = catalog.checksum;
            if (checksum != null && checksum.enabled)
            {
                byte before = work[checksum.byteIndex];
                byte after = checksum.Compute(work);
                if (before != after)
                {
                    work[checksum.byteIndex] = after;
                    changes.Add(new Change
                    {
                        paramId = "$checksum",
                        label = "Block checksum",
                        offset = checksum.byteIndex,
                        oldHex = Hex(new[] { before }),
                        newHex = Hex(new[] { after }),
                        oldDisplay = "0x" + before.ToString("X2"),
                        newDisplay = "0x" + after.ToString("X2"),
                        kind = "checksum"
                    });
                }
            }

            if (changes.Count == 0)
                throw new InvalidDataException("Nothing would change: those values already match the read block.");

            int changed = 0;
            for (int i = 0; i < work.Length; i++) if (work[i] != baseBlock[i]) changed++;

            return new Plan
            {
                id = newId(),
                changes = changes,
                changedBytes = changed,
                summary = changes.Count + " change(s), " + changed + " byte(s)",
                Block = work
            };
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        internal static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}

