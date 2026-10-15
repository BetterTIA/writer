using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TiaMcpServer.ModelContextProtocol;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.Test
{
    /// <summary>
    /// Tests for the write safeguards of the ANet fork that need no project: the content hash
    /// an import compares before writing, and the audit record. These tests do not connect to
    /// TIA Portal.
    /// </summary>
    [TestClass]
    public class Test11Safeguards
    {
        #region content hash

        [TestMethod]
        public void Test_1100_ContentHash_HasTheDocumentedShape()
        {
            var hash = ContentHash.OfBytes(ContentHash.Xml, Encoding.UTF8.GetBytes("<Document />"));

            StringAssert.StartsWith(hash, "xml:sha256:");
            Assert.AreEqual("xml:sha256:".Length + 64, hash.Length);
            Assert.AreEqual(ContentHash.Xml, ContentHash.FormatOf(hash));
        }

        [TestMethod]
        public void Test_1101_ContentHash_IsStableAndSensitive()
        {
            var a = ContentHash.OfBytes(ContentHash.Xml, Encoding.UTF8.GetBytes("abc"));
            var b = ContentHash.OfBytes(ContentHash.Xml, Encoding.UTF8.GetBytes("abc"));
            var c = ContentHash.OfBytes(ContentHash.Xml, Encoding.UTF8.GetBytes("abd"));

            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }

        [TestMethod]
        public void Test_1102_ContentHash_PartsCannotShiftIntoEachOther()
        {
            var ab = ContentHash.OfBytes(ContentHash.Documents, Encoding.UTF8.GetBytes("ab"), Encoding.UTF8.GetBytes("c"));
            var a_bc = ContentHash.OfBytes(ContentHash.Documents, Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("bc"));

            Assert.AreNotEqual(ab, a_bc);
        }

        [TestMethod]
        public void Test_1103_ContentHash_OfFiles_TreatsAMissingFileAsEmpty()
        {
            var directory = Path.Combine(Path.GetTempPath(), "tiamcp-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                var dcl = Path.Combine(directory, "FB_1.s7dcl");
                File.WriteAllText(dcl, "FUNCTION_BLOCK FB_1");

                var withMissingRes = ContentHash.OfFiles(ContentHash.Documents, dcl, Path.Combine(directory, "FB_1.s7res"));
                var expected = ContentHash.OfBytes(ContentHash.Documents, File.ReadAllBytes(dcl), Array.Empty<byte>());

                Assert.AreEqual(expected, withMissingRes);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Test_1104_ContentHash_FormatOf_RejectsMalformedHashes()
        {
            foreach (var bad in new[] { "", "sha256:abc", "xml:md5:" + new string('0', 32), "xml:sha256:" + new string('0', 63), "xml:sha256:" + new string('g', 64) })
            {
                var ex = Assert.ThrowsException<PortalException>(() => ContentHash.FormatOf(bad), bad);
                Assert.AreEqual(PortalErrorCode.InvalidParams, ex.Code);
            }
        }

        [TestMethod]
        public void Test_1105_ContentHash_Verify_NamesTheChangedObject()
        {
            var old = ContentHash.OfBytes(ContentHash.Xml, Encoding.UTF8.GetBytes("v1"));
            var now = ContentHash.OfBytes(ContentHash.Xml, Encoding.UTF8.GetBytes("v2"));

            ContentHash.Verify(old, old, "Station1/FB_Conveyor");
            ContentHash.Verify(" " + old.ToUpperInvariant() + " ", old, "Station1/FB_Conveyor");

            var ex = Assert.ThrowsException<PortalException>(() => ContentHash.Verify(old, now, "Station1/FB_Conveyor"));

            Assert.AreEqual(PortalErrorCode.InvalidState, ex.Code);
            StringAssert.StartsWith(ex.Message, "state_changed:");
            StringAssert.Contains(ex.Message, "Station1/FB_Conveyor");
            StringAssert.Contains(ex.Message, "Nothing was written");
        }

        #endregion

        #region audit

        private static IDictionary<string, JsonElement> Arguments(string json) =>
            JsonDocument.Parse(json).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        [TestMethod]
        public void Test_1110_AuditRecord_IsOneJsonLineWithTheCallAndItsOutcome()
        {
            var started = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);

            var line = WriteAudit.Record("RenameTags", Arguments("{\"softwarePath\":\"PLC_1\",\"renames\":\"A=B\",\"dryRun\":true,\"portalId\":1234}"), started, 42, true, "Dry run: 1 tag(s) would be renamed.");

            Assert.IsFalse(line.Contains("\n"), "one line per call");

            var record = JsonDocument.Parse(line).RootElement;

            Assert.AreEqual("RenameTags", record.GetProperty("tool").GetString());
            Assert.AreEqual(started.ToString("o"), record.GetProperty("time").GetString());
            Assert.IsTrue(record.GetProperty("dryRun").GetBoolean());
            Assert.IsTrue(record.GetProperty("ok").GetBoolean());
            Assert.AreEqual(42, record.GetProperty("durationMs").GetInt64());
            Assert.AreEqual("A=B", record.GetProperty("args").GetProperty("renames").GetString());
            Assert.AreEqual(1234, record.GetProperty("args").GetProperty("portalId").GetInt64());
            Assert.AreEqual("Dry run: 1 tag(s) would be renamed.", record.GetProperty("message").GetString());
        }

        [TestMethod]
        public void Test_1111_AuditRecord_CutsLongArgumentsAndKeepsNoBlockContent()
        {
            var content = new string('x', 5000);

            var line = WriteAudit.Record("ImportXmlBlock", Arguments("{\"xml\":\"" + content + "\",\"dryRun\":\"false\"}"), DateTime.Now, 1, false, new string('m', 1000));
            var record = JsonDocument.Parse(line).RootElement;
            var xml = record.GetProperty("args").GetProperty("xml").GetString()!;

            Assert.IsTrue(xml.Length < 250, "a long argument is cut");
            StringAssert.EndsWith(xml, "...(+4800 chars)");
            Assert.IsFalse(record.GetProperty("dryRun").GetBoolean(), "a string 'false' counts as a real run");
            Assert.IsFalse(record.GetProperty("ok").GetBoolean());
            Assert.IsTrue(record.GetProperty("message").GetString()!.Length < 600, "the message is cut as well");
        }

        [TestMethod]
        public void Test_1112_AuditRecord_WithoutArguments_StillRecordsTheCall()
        {
            var record = JsonDocument.Parse(WriteAudit.Record("SaveProject", null, DateTime.Now, 7, true, null)).RootElement;

            Assert.AreEqual("SaveProject", record.GetProperty("tool").GetString());
            Assert.AreEqual(JsonValueKind.Null, record.GetProperty("dryRun").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, record.GetProperty("message").ValueKind);
            Assert.AreEqual(0, record.GetProperty("args").EnumerateObject().Count());
        }

        [TestMethod]
        public void Test_1113_Audit_TracksEveryWriteToolAndNoReadTool()
        {
            Engineering.TiaMajorVersion = Settings.TiaMajorVersion;
            Openness.Initialize(Engineering.TiaMajorVersion);
            AppDomain.CurrentDomain.AssemblyResolve -= Engineering.Resolver;
            AppDomain.CurrentDomain.AssemblyResolve += Engineering.Resolver;

            Program.BuildTools(allowWrite: true);

            Assert.IsTrue(WriteAudit.IsTracked("RenameTags"));
            Assert.IsTrue(WriteAudit.IsTracked("ConfigureNode"));
            Assert.IsFalse(WriteAudit.IsTracked("GetTags"));
            Assert.IsFalse(WriteAudit.IsTracked(null));
        }

        [TestMethod]
        public void Test_1114_Audit_FileIsMonthlyAndOutsideTheRepository()
        {
            var path = WriteAudit.FilePath(new DateTime(2026, 3, 15));

            StringAssert.EndsWith(path, "writer-audit-2026-03.jsonl");
            Assert.IsFalse(path.StartsWith(Directory.GetCurrentDirectory(), StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
