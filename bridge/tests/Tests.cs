using System;
using System.Collections.Generic;
using System.IO;

namespace P3Orbit.Tests
{
    // Dependency-free test runner for the pure bridge logic. It compiles the
    // production source files directly so offsets, codecs and ISO-TP framing
    // are exercised without an adapter or a vehicle.
    internal static class Tests
    {
        private static int failures;

        private static int Main()
        {
            Console.WriteLine("P3Bench bridge logic tests");

            Check("demo catalogue loads", DemoCatalogoLoads);
            Check("simulator block has a valid checksum", SimulatorBlockChecksums);
            Check("plan recomputes the checksum", PlanRecomputesChecksum);
            Check("plan refuses a corrupt base block", PlanRefusesCorruptBase);
            Check("codec rejects scaled u8 overflow", CodecRejectsScaledU8Overflow);
            Check("codec rejects scaled u16 overflow", CodecRejectsScaledU16Overflow);
            Check("codec round-trips u16 little endian", CodecRoundTripsU16Le);
            Check("catalogue rejects an unknown type", CatalogueRejectsUnknownType);
            Check("catalogue rejects an enum outside one byte", CatalogueRejectsWideEnum);
            Check("catalogue rejects duplicate enum values", CatalogueRejectsDuplicateEnum);
            Check("catalogue rejects an unstoreable max", CatalogueRejectsUnstoreableMax);
            Check("catalogue rejects ascii without a length", CatalogueRejectsAsciiWithoutLength);
            Check("catalogue rejects an inverted range", CatalogueRejectsInvertedRange);
            Check("ISO-TP single frame round-trip", IsoTpSingleFrame);
            Check("ISO-TP multi-frame reassembly", IsoTpMultiFrame);
            Check("ISO-TP rejects a sequence gap", IsoTpRejectsSequenceGap);
            Check("profile parses hex CAN ids", ProfileParsesHex);

            if (failures > 0)
            {
                Console.WriteLine();
                Console.WriteLine(failures + " test(s) failed.");
                return 1;
            }
            Console.WriteLine();
            Console.WriteLine("All bridge logic tests passed.");
            return 0;
        }

        private static Catalog Demo()
        {
            string path = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "catalog", "p3.demo.json"));
            return CatalogIo.Load(path);
        }

        private static void DemoCatalogoLoads()
        {
            var catalog = Demo();
            Equal("p3.demo.synthetic", catalog.id);
            Equal(254, catalog.configLength);
            True(catalog.checksum != null && catalog.checksum.enabled, "checksum should be enabled");
            int count = 0;
            foreach (var p in catalog.AllParams()) count++;
            True(count >= 20, "expected a populated demo catalogue, got " + count + " parameters");
        }

        private static void SimulatorBlockChecksums()
        {
            var catalog = Demo();
            var simulator = new Simulator(catalog);
            byte[] block = simulator.Read();
            Equal(catalog.configLength, block.Length);
            Equal(block[catalog.checksum.byteIndex], catalog.checksum.Compute(block));
        }

        private static void PlanRecomputesChecksum()
        {
            var catalog = Demo();
            var simulator = new Simulator(catalog);
            byte[] before = simulator.Read();
            Param drl = catalog.Find("drlMode");
            int next = ((int)Codec.Read(before, drl) + 1) % drl.values.Count;

            var plan = Plan.Create(catalog, before, new List<Edit>
            {
                new Edit { id = drl.id, value = next }
            }, () => "plan_test");

            True(plan.changedBytes > 0, "plan should change at least one byte");
            Equal(plan.Block[catalog.checksum.byteIndex], catalog.checksum.Compute(plan.Block));
            bool sawChecksum = false;
            foreach (var change in plan.changes)
                if (change.kind == "checksum") sawChecksum = true;
            True(sawChecksum, "plan should include the recomputed checksum");
        }

        private static void PlanRefusesCorruptBase()
        {
            var catalog = Demo();
            byte[] block = new Simulator(catalog).Read();
            block[2] = (byte)(block[2] ^ 0xFF);
            Throws<InvalidDataException>(() => Plan.Create(catalog, block, new List<Edit>
            {
                new Edit { id = "drlMode", value = 2 }
            }, () => "plan_test"), "checksum");
        }

        private static void CodecRejectsScaledU8Overflow()
        {
            var p = new Param
            {
                id = "scaled", label = "Scaled", type = "u8", offset = 0,
                min = 0, max = 200, scale = 0.5
            };
            Throws<InvalidDataException>(() => Codec.Write(new byte[4], p, 200), "cannot be encoded");
        }

        private static void CodecRejectsScaledU16Overflow()
        {
            var p = new Param
            {
                id = "scaled16", label = "Scaled16", type = "u16le", offset = 0,
                min = 0, max = 200000, scale = 0.5
            };
            Throws<InvalidDataException>(() => Codec.Write(new byte[4], p, 200000), "cannot be encoded");
        }

        private static void CodecRoundTripsU16Le()
        {
            var p = new Param
            {
                id = "wide", label = "Wide", type = "u16le", offset = 1,
                min = 0, max = 65535, scale = 1
            };
            byte[] block = new byte[4];
            Codec.Write(block, p, 0x1234);
            Equal(0x34, block[1]);
            Equal(0x12, block[2]);
            Equal(0x1234, (int)Codec.Read(block, p));
        }

        private static void CatalogueRejectsUnknownType()
        {
            Throws<InvalidDataException>(() => CatalogIo.Parse(CatalogJson(
                "{\"id\":\"a\",\"type\":\"u32\",\"offset\":0}")), "unsupported");
        }

        private static void CatalogueRejectsWideEnum()
        {
            Throws<InvalidDataException>(() => CatalogIo.Parse(CatalogJson(
                "{\"id\":\"a\",\"type\":\"enum\",\"offset\":0,\"values\":[{\"value\":300,\"label\":\"wide\"}]}")), "one byte");
        }

        private static void CatalogueRejectsDuplicateEnum()
        {
            Throws<InvalidDataException>(() => CatalogIo.Parse(CatalogJson(
                "{\"id\":\"a\",\"type\":\"enum\",\"offset\":0,\"values\":[{\"value\":1,\"label\":\"a\"},{\"value\":1,\"label\":\"b\"}]}")), "duplicate");
        }

        private static void CatalogueRejectsUnstoreableMax()
        {
            Throws<InvalidDataException>(() => CatalogIo.Parse(CatalogJson(
                "{\"id\":\"a\",\"type\":\"u8\",\"offset\":0,\"max\":300,\"scale\":1}")), "cannot be stored");
        }

        private static void CatalogueRejectsAsciiWithoutLength()
        {
            Throws<InvalidDataException>(() => CatalogIo.Parse(CatalogJson(
                "{\"id\":\"a\",\"type\":\"ascii\",\"offset\":0}")), "ascii length");
        }

        private static void CatalogueRejectsInvertedRange()
        {
            Throws<InvalidDataException>(() => CatalogIo.Parse(CatalogJson(
                "{\"id\":\"a\",\"type\":\"u8\",\"offset\":0,\"min\":20,\"max\":10}")), "greater than min");
        }

        private static string CatalogJson(string parameter)
        {
            return "{\"id\":\"test\",\"configLength\":8,\"groups\":[{\"id\":\"g\",\"label\":\"G\",\"params\":[" +
                parameter + "]}]}";
        }

        private static void IsoTpSingleFrame()
        {
            byte[] frame = IsoTp.SingleFrame(new byte[] { 0x22, 0xF1, 0x90 });
            var reassembler = new IsoTp.Reassembler();
            byte[] complete;
            True(reassembler.Add(frame, out complete), "single frame should complete");
            Equal(3, complete.Length);
            Equal(0x22, complete[0]);
            Equal(0x90, complete[2]);
        }

        private static void IsoTpMultiFrame()
        {
            var reassembler = new IsoTp.Reassembler();
            byte[] complete;

            var first = new byte[8];
            first[0] = 0x10; // 12-byte message
            first[1] = 12;
            for (int i = 0; i < 6; i++) first[2 + i] = (byte)i;
            True(!reassembler.Add(first, out complete), "first frame is not complete");

            var second = new byte[7];
            second[0] = 0x21;
            for (int i = 0; i < 6; i++) second[1 + i] = (byte)(6 + i);
            True(reassembler.Add(second, out complete), "second frame completes the message");
            Equal(12, complete.Length);
            Equal(0, complete[0]);
            Equal(11, complete[11]);
        }

        private static void IsoTpRejectsSequenceGap()
        {
            var reassembler = new IsoTp.Reassembler();
            byte[] complete;
            var first = new byte[8];
            first[0] = 0x10;
            first[1] = 12;
            reassembler.Add(first, out complete);

            var second = new byte[7];
            second[0] = 0x22; // expected 0x21
            Throws<InvalidDataException>(() => reassembler.Add(second, out complete), "sequence");
        }

        private static void ProfileParsesHex()
        {
            Equal(0x726u, Profile.ParseHex("0x726", 0));
            Equal(0x72Eu, Profile.ParseHex("1838", 0));
            Equal(0x7E0u, Profile.ParseHex("0x7E0", 0));
        }

        // ---------- assertions ----------

        private static void Check(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("  ok   " + name);
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine("  FAIL " + name + ": " + ex.Message);
            }
        }

        private static void True(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception("expected '" + expected + "', got '" + actual + "'");
        }

        private static void Throws<T>(Action action, string contains) where T : Exception
        {
            try
            {
                action();
            }
            catch (T ex)
            {
                if (!String.IsNullOrEmpty(contains) &&
                    ex.Message.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0)
                    throw new Exception("exception did not mention '" + contains + "': " + ex.Message);
                return;
            }
            catch (Exception ex)
            {
                throw new Exception("expected " + typeof(T).Name + ", got " + ex.GetType().Name + ": " + ex.Message);
            }
            throw new Exception("expected " + typeof(T).Name + " but nothing was thrown");
        }
    }
}
