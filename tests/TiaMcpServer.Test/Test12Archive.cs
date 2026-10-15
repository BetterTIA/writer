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
        public void Test_1202_NormalizeKind_KnowsFullAndInterim()
        {
            Assert.AreEqual(ArchiveRules.Full, ArchiveRules.NormalizeKind("FULL"));
            Assert.AreEqual(ArchiveRules.Interim, ArchiveRules.NormalizeKind("interim"));
            Assert.ThrowsException<PortalException>(() => ArchiveRules.NormalizeKind("snapshot"));
        }

        [TestMethod]
        public void Test_1203_FileName_FollowsTheNamingRule()
        {
            var time = new DateTime(2026, 10, 15, 14, 30, 59);

            Assert.AreEqual("Line1_V1.2.0.zap21", ArchiveRules.FileName("Line1", "V1.2.0", ArchiveRules.Full, time, 21));
            Assert.AreEqual("Line1_V1.2.0_2026-10-15_1430.zap21", ArchiveRules.FileName("Line1", "V1.2.0", ArchiveRules.Interim, time, 21));
        }

        [TestMethod]
        public void Test_1204_Parse_RoundTripsBothKinds()
        {
            var full = ArchiveRules.Parse(@"D:\archives\Line1_V1.2.0.zap21")!;
            var interim = ArchiveRules.Parse(@"D:\archives\Line_1_V1.2.0_2026-10-15_1430.ZAP21")!;

            Assert.AreEqual("Line1", full.Project);
            Assert.AreEqual("V1.2.0", full.Version);
            Assert.AreEqual(ArchiveRules.Full, full.Kind);
            Assert.IsNull(full.Time);

            Assert.AreEqual("Line_1", interim.Project, "the project name may contain underscores");
            Assert.AreEqual(ArchiveRules.Interim, interim.Kind);
            Assert.AreEqual(new DateTime(2026, 10, 15, 14, 30, 0), interim.Time);
        }

        [TestMethod]
        public void Test_1205_Parse_IgnoresEverythingElse()
        {
            foreach (var other in new[] { "Line1.zap21", "Line1_V1.2.zap21", "Line1_V1.2.0.zip", "Line1_V1.2.0_2026-13-45_9999.zap21", "Line1_V1.2.0_backup.zap21", "notes.txt" })
            {
                Assert.IsNull(ArchiveRules.Parse(other), other);
            }
        }

        [TestMethod]
        public void Test_1206_Plan_KeepsEveryFullAndTheNewestThreeInterim()
        {
            var files = new[]
            {
                "Line1_V1.0.0.zap21",
                "Line1_V1.1.0.zap21",
                "Line1_V1.2.0_2026-10-13_0900.zap21",
                "Line1_V1.2.0_2026-10-14_0900.zap21",
                "Line1_V1.2.0_2026-10-15_0900.zap21",
                "Line1_V1.2.0_2026-10-15_1430.zap21",
                "Line1_V1.2.0_2026-10-12_1700.zap21",
                "Line2_V1.2.0_2026-10-11_0900.zap21",
                "Line1_V1.2.0_backup.zap21",
                "readme.txt"
            };

            var (keep, delete) = ArchiveRules.Plan(files, "line1");

            CollectionAssert.AreEquivalent(
                new[] { "Line1_V1.0.0.zap21", "Line1_V1.1.0.zap21", "Line1_V1.2.0_2026-10-15_1430.zap21", "Line1_V1.2.0_2026-10-15_0900.zap21", "Line1_V1.2.0_2026-10-14_0900.zap21" },
                keep.Select(a => a.Path).ToArray());
            CollectionAssert.AreEqual(
                new[] { "Line1_V1.2.0_2026-10-13_0900.zap21", "Line1_V1.2.0_2026-10-12_1700.zap21" },
                delete.Select(a => a.Path).ToArray(),
                "older interim archives, oldest last; another project and unmatched names are never listed");
        }

        [TestMethod]
        public void Test_1207_Plan_WithThreeOrFewerInterim_DeletesNothing()
        {
            var (keep, delete) = ArchiveRules.Plan(new[] { "Line1_V1.2.0_2026-10-13_0900.zap21", "Line1_V1.2.0_2026-10-14_0900.zap21" }, "Line1");

            Assert.AreEqual(2, keep.Count);
            Assert.AreEqual(0, delete.Count);
        }
    }
}
