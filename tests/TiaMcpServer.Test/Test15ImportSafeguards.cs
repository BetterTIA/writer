using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.Test
{
    /// <summary>
    /// Round 5 safeguards that need no TIA Portal: the content hash over a normalized export,
    /// the error detail of a failed import, the absolute-folder check and the tool counts the
    /// README states.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class Test15ImportSafeguards
    {
        private static string Block(string created, string modified, string code) =>
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Document>\r\n  <Engineering version=\"V21\" />\r\n" +
            $"  <DocumentInfo><Created>{created}</Created><ExportSetting>WithDefaults</ExportSetting></DocumentInfo>\r\n" +
            $"  <SW.Blocks.FB ID=\"0\">\r\n    <AttributeList>\r\n      <Name>FB_1</Name>\r\n      <ParameterModified>{modified}</ParameterModified>\r\n      <InterfaceModifiedDate>{modified}</InterfaceModifiedDate>\r\n      <CodeModifiedDate>{modified}</CodeModifiedDate>\r\n    </AttributeList>\r\n" +
            $"    <ObjectList><SW.Blocks.CompileUnit ID=\"1\"><Code>{code}</Code></SW.Blocks.CompileUnit></ObjectList>\r\n  </SW.Blocks.FB>\r\n</Document>";

        private static string HashOf(string xml)
        {
            var file = Path.GetTempFileName();

            try
            {
                File.WriteAllBytes(file, Encoding.UTF8.GetBytes(xml));
                return ContentHash.OfXmlFile(file);
            }
            finally
            {
                File.Delete(file);
            }
        }

        [TestMethod]
        public void Test_1500_XmlHash_IgnoresTimestampsAndLineEnds()
        {
            var first = HashOf(Block("2026-11-18T10:00:00.1Z", "2026-11-18T09:00:00.0Z", "A AND B"));
            var second = HashOf(Block("2026-11-18T12:30:59.9Z", "2026-11-17T01:02:03.4Z", "A AND B").Replace("\r\n", "\n"));

            Assert.AreEqual(first, second);
        }

        [TestMethod]
        public void Test_1501_XmlHash_SeesAChangeInTheCode()
        {
            Assert.AreNotEqual(
                HashOf(Block("2026-11-18T10:00:00Z", "t", "A AND B")),
                HashOf(Block("2026-11-18T10:00:00Z", "t", "A OR B")));
        }

        [TestMethod]
        public void Test_1502_XmlHash_FallsBackToTextForNonXml()
        {
            Assert.AreEqual(HashOf("not xml\r\nline"), HashOf("not xml\nline"));
            Assert.AreNotEqual(HashOf("not xml"), HashOf("not xml!"));
        }

        [TestMethod]
        public void Test_1510_ErrorDetail_ListsTheWholeChain()
        {
            var ex = new InvalidOperationException("Failed importing 'FB_1'", new ApplicationException("Openness: block is know-how protected", new ArgumentException("inner-most")));

            var text = ErrorDetail.Append("Unexpected error: Failed importing 'FB_1'", ex);

            StringAssert.Contains(text, "errorDetail: [");
            StringAssert.Contains(text, "Openness: block is know-how protected");
            StringAssert.Contains(text, "inner-most");
            Assert.AreEqual(1, Regex.Matches(text, "Failed importing 'FB_1'").Count, "the outer message is not repeated");
        }

        [TestMethod]
        public void Test_1511_ErrorDetail_LeavesAMessageWithoutDetailAlone()
        {
            Assert.AreEqual("plain", ErrorDetail.Append("plain", new Exception("plain")));
            Assert.AreEqual("plain", ErrorDetail.Append("plain", null));
        }

        [TestMethod]
        public void Test_1512_ErrorDetail_AddsImportMessages()
        {
            var text = ErrorDetail.Append("Failed", new Exception("Failed"), new[] { "FB_1: Failure. Interface mismatch" });

            StringAssert.Contains(text, "FB_1: Failure. Interface mismatch");
        }

        [TestMethod]
        public void Test_1520_RequireAbsoluteFolder_RefusesRelativeAndDriveRelative()
        {
            Portal.RequireAbsoluteFolder(@"C:\Temp\TextLists");
            Portal.RequireAbsoluteFolder(@"\\server\share\TextLists");

            foreach (var bad in new[] { "", "TextLists", @"\TextLists", @"C:TextLists", @"..\x" })
            {
                Assert.ThrowsException<PortalException>(() => Portal.RequireAbsoluteFolder(bad), bad);
            }
        }

        [TestMethod]
        public void Test_1530_Readme_ReadOnlyToolCount_MatchesTheServer()
        {
            Engineering.TiaMajorVersion = Settings.TiaMajorVersion;
            Openness.Initialize(Engineering.TiaMajorVersion);
            AppDomain.CurrentDomain.AssemblyResolve -= Engineering.Resolver;
            AppDomain.CurrentDomain.AssemblyResolve += Engineering.Resolver;

            var actual = Program.BuildTools(allowWrite: false).Count();
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir != null && !(File.Exists(Path.Combine(dir.FullName, "README.md")) && File.ReadAllText(Path.Combine(dir.FullName, "README.md")).Contains("Read-only tools ("))) 
            {
                dir = dir.Parent;
            }

            Assert.IsNotNull(dir, "README.md not found above the test folder");

            var match = Regex.Match(File.ReadAllText(Path.Combine(dir!.FullName, "README.md")), @"Read-only tools \((\d+)\)");

            Assert.IsTrue(match.Success, "README.md must state 'Read-only tools (N)'");
            Assert.AreEqual(actual, int.Parse(match.Groups[1].Value), "README count of read-only tools is out of date");
        }
    }
}
