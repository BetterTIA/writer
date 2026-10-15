using System;
using System.Linq;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.Test
{
    /// <summary>
    /// Tests for the naming and retention of project archives (ArchiveRules). These tests do
    /// not connect to TIA Portal.
    /// </summary>
    [TestClass]
    public class Test12Archive
    {
        [TestMethod]
        public void Test_1200_NormalizeVersion_AcceptsWithAndWithoutPrefix()
        {
            Assert.AreEqual("V1.2.3", ArchiveRules.NormalizeVersion("1.2.3"));
            Assert.AreEqual("V1.2.3", ArchiveRules.NormalizeVersion("V1.2.3"));
            Assert.AreEqual("V1.2.3", ArchiveRules.NormalizeVersion(" v1.2.3 "));
        }

        [TestMethod]
        public void Test_1201_NormalizeVersion_RefusesAnythingElse()
        {
            foreach (var bad in new[] { "", "1.2", "1.2.3.4", "V1", "latest", "1,2,3" })
            {
                var ex = Assert.ThrowsException<PortalException>(() => ArchiveRules.NormalizeVersion(bad), bad);
                Assert.AreEqual(PortalErrorCode.InvalidParams, ex.Code);
            }
        }

        [TestMethod]
        public void Test_1202_KindOf_PatchZeroIsFull()
        {
            Assert.AreEqual(ArchiveRules.Full, ArchiveRules.KindOf("V2.3.0"));
            Assert.AreEqual(ArchiveRules.Interim, ArchiveRules.KindOf("V2.2.1"));
            Assert.AreEqual(ArchiveRules.Interim, ArchiveRules.KindOf("V2.2.10"));
        }

        [TestMethod]
        public void Test_1203_FileName_PutsTheTimeBeforeTheVersion()
        {
            var time = new DateTime(2026, 10, 15, 14, 30, 59);

            Assert.AreEqual("Line1_2026-10-15_1430_V2.3.0.zap21", ArchiveRules.FileName("Line1", "V2.3.0", time, 21));
            Assert.AreEqual("Line1_2026-10-15_1430_V2.2.1.zap21", ArchiveRules.FileName("Line1", "V2.2.1", time, 21));
        }

        [TestMethod]
        public void Test_1204_Parse_RoundTripsBothKinds()
        {
            var full = ArchiveRules.Parse(@"D:\archives\Line1_2026-10-15_1430_V2.3.0.zap21")!;
            var interim = ArchiveRules.Parse(@"D:\archives\Line_1_2026-10-15_1430_V2.2.1.ZAP21")!;

            Assert.AreEqual("Line1", full.Project);
            Assert.AreEqual("V2.3.0", full.Version);
            Assert.AreEqual(ArchiveRules.Full, full.Kind);
            Assert.AreEqual(new DateTime(2026, 10, 15, 14, 30, 0), full.Time);

            Assert.AreEqual("Line_1", interim.Project, "the project name may contain underscores");
            Assert.AreEqual(ArchiveRules.Interim, interim.Kind);
        }

        [TestMethod]
        public void Test_1205_Parse_IgnoresEverythingElse()
        {
            foreach (var other in new[] { "Line1.zap21", "Line1_V1.2.0.zap21", "Line1_2026-10-15_1430_V1.2.zap21", "Line1_2026-10-15_1430_V1.2.0.zip", "Line1_2026-13-45_9999_V1.2.0.zap21", "Line1_V1.2.0_2026-10-15_1430.zap21", "notes.txt" })
            {
                Assert.IsNull(ArchiveRules.Parse(other), other);
            }
        }

        [TestMethod]
        public void Test_1206_Plan_KeepsTheNewestFullAndTheThreeNewestInterim()
        {
            var files = new[]
            {
                "Line1_2026-10-01_0900_V2.1.0.zap21",
                "Line1_2026-10-10_0900_V2.2.0.zap21",
                "Line1_2026-10-12_1700_V2.2.1.zap21",
                "Line1_2026-10-13_0900_V2.2.2.zap21",
                "Line1_2026-10-14_0900_V2.2.3.zap21",
                "Line1_2026-10-15_0900_V2.2.4.zap21",
                "Line1_2026-10-15_1430_V2.2.5.zap21",
                "Line2_2026-10-11_0900_V2.2.1.zap21",
                "Line1_V1.2.0_backup.zap21",
                "readme.txt"
            };

            var plan = ArchiveRules.Plan(files, "line1");
            var keep = plan.Keep;
            var delete = plan.Delete;

            CollectionAssert.AreEquivalent(
                new[] { "Line1_2026-10-10_0900_V2.2.0.zap21", "Line1_2026-10-15_1430_V2.2.5.zap21", "Line1_2026-10-15_0900_V2.2.4.zap21", "Line1_2026-10-14_0900_V2.2.3.zap21" },
                keep.Select(a => a.Path).ToArray());
            CollectionAssert.AreEquivalent(
                new[] { "Line1_2026-10-01_0900_V2.1.0.zap21", "Line1_2026-10-13_0900_V2.2.2.zap21", "Line1_2026-10-12_1700_V2.2.1.zap21" },
                delete.Select(a => a.Path).ToArray(),
                "the older full version and the older interim archives; another project and unmatched names are never listed");
        }

        [TestMethod]
        public void Test_1207_Plan_WithLittleToPrune_DeletesNothing()
        {
            var plan = ArchiveRules.Plan(new[] { "Line1_2026-10-13_0900_V2.2.1.zap21", "Line1_2026-10-14_0900_V2.2.2.zap21", "Line1_2026-10-10_0900_V2.2.0.zap21" }, "Line1");
            var keep = plan.Keep;
            var delete = plan.Delete;

            Assert.AreEqual(3, keep.Count);
            Assert.AreEqual(0, delete.Count);
        }

        [TestMethod]
        public void Test_1208_Plan_OrdersByTimeAndKeepsAConflictingHigherVersion()
        {
            // The newest archives by time carry lower versions than an older one (a typo, or work on an older branch).
            var files = new[]
            {
                "Line1_2026-10-15_1000_V2.3.0.zap21",   // older by time, but the highest full version
                "Line1_2026-10-15_1200_V2.2.0.zap21",   // newest full by time
                "Line1_2026-10-14_0900_V2.2.9.zap21",   // older by time, higher interim version
                "Line1_2026-10-15_1300_V2.2.1.zap21",
                "Line1_2026-10-15_1400_V2.2.2.zap21",
                "Line1_2026-10-15_1500_V2.2.3.zap21",
                "Line1_2026-10-13_0900_V2.1.5.zap21"    // older by time and lower than every kept interim: goes
            };

            var plan = ArchiveRules.Plan(files, "Line1");

            CollectionAssert.AreEquivalent(
                new[] { "Line1_2026-10-15_1200_V2.2.0.zap21", "Line1_2026-10-15_1500_V2.2.3.zap21", "Line1_2026-10-15_1400_V2.2.2.zap21", "Line1_2026-10-15_1300_V2.2.1.zap21",
                        "Line1_2026-10-15_1000_V2.3.0.zap21", "Line1_2026-10-14_0900_V2.2.9.zap21" },
                plan.Keep.Select(a => a.Path).ToArray(), "time decides; the two archives where version and time disagree are kept and reported");
            CollectionAssert.AreEqual(new[] { "Line1_2026-10-13_0900_V2.1.5.zap21" }, plan.Delete.Select(a => a.Path).ToArray());
            Assert.AreEqual(2, plan.Warnings.Count);
            StringAssert.Contains(plan.Warnings[0], "V2.3.0");
        }
    }
}
