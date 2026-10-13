using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using TiaMcpServer.ModelContextProtocol;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.Test
{
    /// <summary>
    /// Tests for the ANet fork edits on exported SimaticML (network texts, member rename) and for
    /// the lookup of names that contain '/'. The documents are built here in the shape TIA Portal
    /// V21 exports, namespaces included. These tests do not connect to TIA Portal.
    /// </summary>
    [TestClass]
    public class Test9SimaticMl
    {
        private const string InterfaceNs = "http://www.siemens.com/automation/Openness/SW/Interface/v5";
        private const string FlgNetNs = "http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v5";
        private const string GraphNs = "http://www.siemens.com/automation/Openness/SW/NetworkSource/Graph/v6";

        private static readonly Dictionary<string, string> NoTags = new Dictionary<string, string>();

        #region document builders

        private static XDocument Doc(string kind, string name, string sections, string networks = "", string attributes = "")
        {
            return XDocument.Parse(
                $"<Document><Engineering version=\"V21\" /><{kind} ID=\"0\"><AttributeList>{attributes}" +
                $"<Interface><Sections xmlns=\"{InterfaceNs}\">{sections}</Sections></Interface><Name>{name}</Name>" +
                $"</AttributeList><ObjectList>{networks}</ObjectList></{kind}></Document>");
        }

        private static string Section(string name, string members) => $"<Section Name=\"{name}\">{members}</Section>";

        private static string Member(string name, string datatype, string inner = "") =>
            $"<Member Name=\"{name}\" Datatype=\"{datatype.Replace("\"", "&quot;")}\">{inner}</Member>";

        private static string Text(string composition, string value) =>
            $"<MultilingualText ID=\"2\" CompositionName=\"{composition}\"><ObjectList>" +
            "<MultilingualTextItem ID=\"3\" CompositionName=\"Items\"><AttributeList>" +
            $"<Culture>en-US</Culture><Text>{value}</Text></AttributeList></MultilingualTextItem></ObjectList></MultilingualText>";

        private static string Network(string source, string title = "", string comment = "") =>
            "<SW.Blocks.CompileUnit ID=\"1\" CompositionName=\"CompileUnits\"><AttributeList>" +
            $"<NetworkSource>{source}</NetworkSource></AttributeList>" +
            $"<ObjectList>{Text("Comment", comment)}{Text("Title", title)}</ObjectList></SW.Blocks.CompileUnit>";

        private static string Lad(string parts, string wires = "") =>
            $"<FlgNet xmlns=\"{FlgNetNs}\"><Parts>{parts}</Parts><Wires>{wires}</Wires></FlgNet>";

        private static string Access(string scope, params string[] components) =>
            $"<Access Scope=\"{scope}\" UId=\"21\"><Symbol>" +
            string.Concat(components.Select(c => $"<Component Name=\"{c}\" />")) + "</Symbol></Access>";

        private static Dictionary<string, XDocument> Docs(params XDocument[] docs) =>
            docs.ToDictionary(SimaticMl.ObjectName, d => d, StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, string> Renames(string renames) =>
            new Dictionary<string, string>(McpServer.ParseRenames(renames));

        private static IDictionary<string, string> Texts(string replacements) =>
            McpServer.ParseRenames(replacements, StringComparer.Ordinal);

        /// <summary>Every operand of the document as "A.B.C", in document order.</summary>
        private static List<string> Operands(XDocument doc) =>
            doc.Descendants()
                .Where(e => e.Name.LocalName == "Symbol" || e.Name.LocalName == "Instance")
                .Select(s => string.Join(".", s.Elements().Where(c => c.Name.LocalName == "Component").Select(c => (string)c.Attribute("Name")!)))
                .ToList();

        #endregion

        #region network texts

        [TestMethod]
        public void SetNetworkText_ChangesOnlyTheChosenNetwork()
        {
            var doc = Doc("SW.Blocks.FB", "FB_Conveyor", "", Network(Lad(""), "Start", "old") + Network(Lad(""), "Stop", "old"));

            var changes = SimaticMl.SetNetworkText(doc, 2, "Halt", null, "");

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("network 2 Title [en-US]", changes[0].Where);
            Assert.AreEqual("Stop", changes[0].Old);
            CollectionAssert.AreEqual(
                new[] { "old", "Start", "old", "Halt" },
                doc.Descendants("Text").Select(t => t.Value).ToList());
        }

        [TestMethod]
        public void SetNetworkText_UnknownNetworkOrCulture_Throws()
        {
            var doc = Doc("SW.Blocks.FB", "FB_Conveyor", "", Network(Lad(""), "Start"));

            Assert.ThrowsException<PortalException>(() => SimaticMl.SetNetworkText(doc, 2, "x", null, ""));
            Assert.ThrowsException<PortalException>(() => SimaticMl.SetNetworkText(doc, 1, "x", null, "de-DE"));
        }

        [TestMethod]
        public void ReplaceNetworkText_ReplacesInTitlesAndCommentsOfEveryNetwork()
        {
            var doc = Doc("SW.Blocks.FB", "FB_Pumps", "",
                Network(Lad(""), "Pump 1 start", "Starts pump 1") + Network(Lad(""), "Valve", "unrelated"));

            var changes = SimaticMl.ReplaceNetworkText(doc, Texts("ump 1=ump 2"), "");

            Assert.AreEqual(2, changes.Count);
            CollectionAssert.AreEqual(
                new[] { "Starts pump 2", "Pump 2 start", "unrelated", "Valve" },
                doc.Descendants("Text").Select(t => t.Value).ToList());
        }

        [TestMethod]
        public void ReplaceAll_SwapsInOnePassAndPrefersTheLongerMatch()
        {
            var pairs = Texts("Pump 1=Pump 2; Pump 2=Pump 1; Pump 10=Pump 20");

            Assert.AreEqual("Pump 2, Pump 1, Pump 20, pump 1", SimaticMl.ReplaceAll("Pump 1, Pump 2, Pump 10, pump 1", pairs));
            Assert.ThrowsException<PortalException>(() => SimaticMl.ReplaceAll("x", new Dictionary<string, string>()));
        }

        [TestMethod]
        public void ReplaceNetworkText_ScopeAll_AddsBlockTextsAndGraphTextsButNotStepNames()
        {
            var graph =
                $"<Graph xmlns=\"{GraphNs}\"><Sequence><Comment><MultiLanguageText Lang=\"en-US\">Pump 1 sequence</MultiLanguageText></Comment>" +
                "<Steps><Step Number=\"3\" Name=\"Pump 1 on\"><Actions><Title><MultiLanguageText Lang=\"en-US\">Start pump 1</MultiLanguageText></Title>" +
                "</Actions></Step></Steps><Transitions><Transition Number=\"4\" Name=\"T4\">" +
                "<Comment><MultiLanguageText Lang=\"en-US\">pump 1 runs</MultiLanguageText></Comment></Transition></Transitions></Sequence></Graph>";
            string Build() => Doc("SW.Blocks.FB", "FB_Sequence",
                Section("Static", Member("X", "Bool", "<Comment><MultiLanguageText Lang=\"en-US\">pump 1 flag</MultiLanguageText></Comment>")),
                Text("Comment", "Controls pump 1") + Text("Title", "pump 1") + Network(graph, "pump 1 steps")).ToString();
            var networksOnly = XDocument.Parse(Build());
            var all = XDocument.Parse(Build());

            var few = SimaticMl.ReplaceNetworkText(networksOnly, Texts("ump 1=ump 2"), "");
            var many = SimaticMl.ReplaceNetworkText(all, Texts("ump 1=ump 2"), "", all: true);

            CollectionAssert.AreEqual(new[] { "network 1 Title [en-US]" }, few.Select(c => c.Where).ToList());
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "block Comment [en-US]", "block Title [en-US]", "network 1 Title [en-US]",
                    "network 1 sequence Comment [en-US]", "network 1 step 3 Title [en-US]", "network 1 transition 4 Comment [en-US]"
                },
                many.Select(c => c.Where).ToList());

            // A step name is an interface member, and a member comment is not a network text.
            StringAssert.Contains(all.ToString(), "Name=\"Pump 1 on\"");
            StringAssert.Contains(all.ToString(), "pump 1 flag");
        }

        #endregion

        #region member rename

        [TestMethod]
        public void RenameMembers_GlobalDb_RenamesOnlyAccessesThatResolveToTheMember()
        {
            var line = Doc("SW.Blocks.GlobalDB", "DB_Line", Section("Static",
                Member("Speed", "Real") + Member("Config", "Struct", Member("Speed", "Real"))));
            var other = Doc("SW.Blocks.GlobalDB", "DB_Other", Section("Static", Member("Speed", "Real")));
            var user = Doc("SW.Blocks.FB", "FB_User", Section("Input", Member("Speed", "Real")), Network(Lad(
                Access("GlobalVariable", "DB_Line", "Speed") +
                Access("GlobalVariable", "DB_Line", "Config", "Speed") +
                Access("GlobalVariable", "DB_Other", "Speed") +
                Access("LocalVariable", "Speed"))));

            var result = SimaticMl.RenameMembers(Docs(line, other, user), NoTags, "DB_Line", Renames("Speed=Velocity"));

            CollectionAssert.AreEqual(
                new[] { "DB_Line.Velocity", "DB_Line.Config.Speed", "DB_Other.Speed", "Speed" }, Operands(user));
            CollectionAssert.AreEquivalent(new[] { "DB_Line", "FB_User" }, result.Changed.ToList());
            Assert.AreEqual(0, result.Warnings.Count);
            Assert.AreEqual("\"DB_Line\".Velocity", result.Changes.Single(c => c.Object == "FB_User").New);
            Assert.AreEqual("network 1", result.Changes.Single(c => c.Object == "FB_User").Where);
            StringAssert.Contains(line.ToString(), "Name=\"Velocity\"");
            StringAssert.Contains(other.ToString(), "Name=\"Speed\"");
        }

        [TestMethod]
        public void RenameMembers_Swap_IsAppliedInOnePass()
        {
            var db = Doc("SW.Blocks.GlobalDB", "DB_Line", Section("Static", Member("Left", "Bool") + Member("Right", "Bool")));
            var user = Doc("SW.Blocks.FC", "FC_User", "", Network(Lad(
                Access("GlobalVariable", "DB_Line", "Left") + Access("GlobalVariable", "DB_Line", "Right"))));

            SimaticMl.RenameMembers(Docs(db, user), NoTags, "DB_Line", Renames("Left=Right; Right=Left"));

            CollectionAssert.AreEqual(new[] { "DB_Line.Right", "DB_Line.Left" }, Operands(user));
        }

        [TestMethod]
        public void RenameMembers_CollisionOrMemberOfAnotherType_Throws()
        {
            var motor = Doc("SW.Types.PlcStruct", "Motor", Section("None", Member("Speed", "Real")));
            var db = Doc("SW.Blocks.GlobalDB", "DB_Line", Section("Static",
                Member("Left", "Bool") + Member("Right", "Bool") + Member("Drive", "\"Motor\"")));

            var collision = Assert.ThrowsException<PortalException>(
                () => SimaticMl.RenameMembers(Docs(db, motor), NoTags, "DB_Line", Renames("Left=right")));
            var foreign = Assert.ThrowsException<PortalException>(
                () => SimaticMl.RenameMembers(Docs(db, motor), NoTags, "DB_Line", Renames("Drive.Speed=Velocity")));

            Assert.AreEqual(PortalErrorCode.InvalidParams, collision.Code);
            Assert.AreEqual(PortalErrorCode.NotFound, foreign.Code);
            StringAssert.Contains(db.ToString(), "Name=\"Left\"");
        }

        [TestMethod]
        public void RenameMembers_TypeMember_FollowsTheTypeThroughDbArrayTagAndLocal()
        {
            var motor = Doc("SW.Types.PlcStruct", "Motor", Section("None", Member("Speed", "Real") + Member("Run", "Bool")));
            var line = Doc("SW.Blocks.GlobalDB", "DB_Line", Section("Static",
                Member("Drive", "\"Motor\"") + Member("Drives", "Array[0..3] of \"Motor\"")));
            var copy = $"<Sections>{Section("None", Member("Speed", "Real", "<StartValue>5.0</StartValue>") + Member("Run", "Bool"))}</Sections>";
            var user = Doc("SW.Blocks.FB", "FB_User",
                Section("Static", Member("M", "\"Motor\"", copy) + Member("Other", "Struct", Member("Speed", "Real"))),
                Network(Lad(
                    Access("GlobalVariable", "DB_Line", "Drive", "Speed") +
                    "<Access Scope=\"GlobalVariable\" UId=\"22\"><Symbol><Component Name=\"DB_Line\" />" +
                    "<Component Name=\"Drives\" AccessModifier=\"Array\">" + Access("LocalVariable", "Other", "Speed") + "</Component>" +
                    "<Component Name=\"Speed\" /></Symbol></Access>" +
                    Access("GlobalVariable", "MotorTag", "Speed") +
                    Access("LocalVariable", "M", "Speed"))));
            var tags = new Dictionary<string, string> { ["MotorTag"] = "\"Motor\"" };

            var result = SimaticMl.RenameMembers(Docs(motor, line, user), tags, "Motor", Renames("Speed=Velocity"));

            CollectionAssert.AreEqual(
                new[] { "DB_Line.Drive.Velocity", "DB_Line.Drives.Velocity", "Other.Speed", "MotorTag.Velocity", "M.Velocity" },
                Operands(user));
            Assert.AreEqual(0, result.Warnings.Count);

            // The copy of the type inside FB_User follows, and its own start value is reported.
            var copied = user.Descendants().Where(e => e.Name.LocalName == "Member" && (string?)e.Attribute("Name") == "M")
                .Descendants().Where(e => e.Name.LocalName == "Member").Select(e => (string)e.Attribute("Name")!).ToList();
            CollectionAssert.AreEqual(new[] { "Velocity", "Run" }, copied);
            Assert.IsTrue(result.Changes.Any(c => c.Object == "FB_User" && c.Where == "interface" && c.New == "start value of Velocity"));

            // DB_Line only holds members of the type, no reference and no start value: no import needed.
            CollectionAssert.AreEquivalent(new[] { "Motor", "FB_User" }, result.Changed.ToList());
        }

        [TestMethod]
        public void RenameMembers_BlockParameter_RenamesCallPinsInstanceAccessAndBody()
        {
            var conveyor = Doc("SW.Blocks.FB", "FB_Conveyor", Section("Input", Member("Enable", "Bool")),
                Network(Lad(Access("LocalVariable", "Enable"))));
            var instance = Doc("SW.Blocks.InstanceDB", "DB_Conv1", Section("Input", Member("Enable", "Bool")),
                attributes: "<InstanceOfName>FB_Conveyor</InstanceOfName>");
            var main = Doc("SW.Blocks.FB", "FB_Main", Section("Static", Member("Conv", "\"FB_Conveyor\"")), Network(Lad(
                "<Call UId=\"30\"><CallInfo Name=\"FB_Conveyor\" BlockType=\"FB\">" +
                "<Instance Scope=\"GlobalVariable\" UId=\"31\"><Component Name=\"DB_Conv1\" /></Instance>" +
                "<Parameter Name=\"Enable\" Section=\"Input\" Type=\"Bool\" /></CallInfo></Call>" +
                "<Part Name=\"Contact\" UId=\"32\" />" +
                Access("GlobalVariable", "DB_Conv1", "Enable") +
                Access("LocalVariable", "Conv", "Enable"),
                "<Wire UId=\"40\"><NameCon UId=\"30\" Name=\"Enable\" /></Wire><Wire UId=\"41\"><NameCon UId=\"32\" Name=\"Enable\" /></Wire>")));

            var result = SimaticMl.RenameMembers(Docs(conveyor, instance, main), NoTags, "FB_Conveyor", Renames("Enable=Release"));

            CollectionAssert.AreEqual(new[] { "Release" }, Operands(conveyor));
            CollectionAssert.AreEqual(new[] { "DB_Conv1", "DB_Conv1.Release", "Conv.Release" }, Operands(main));
            Assert.AreEqual("Release", (string)main.Descendants().Single(e => e.Name.LocalName == "Parameter").Attribute("Name")!);

            // Only the wire of the call is a pin of FB_Conveyor; UId 32 is another instruction.
            CollectionAssert.AreEqual(
                new[] { "Release", "Enable" },
                main.Descendants().Where(e => e.Name.LocalName == "NameCon").Select(e => (string)e.Attribute("Name")!).ToList());
            CollectionAssert.AreEquivalent(new[] { "FB_Conveyor", "FB_Main" }, result.Changed.ToList());
        }

        [TestMethod]
        public void RenameMembers_Dependents_AreTheBlocksThatReadTheChangedInterfaceEvenAnotherMember()
        {
            // The live case: an F-FB reads a member of the instance DB that is not renamed at all.
            var conveyor = Doc("SW.Blocks.FB", "FB_Conveyor", Section("Input", Member("Enable", "Bool")) + Section("Output", Member("Running", "Bool")));
            var instance = Doc("SW.Blocks.InstanceDB", "DB_Conv1", Section("Output", Member("Running", "Bool")),
                attributes: "<InstanceOfName>FB_Conveyor</InstanceOfName>");
            var safety = Doc("SW.Blocks.FB", "F_Guard", "", Network(Lad(Access("GlobalVariable", "DB_Conv1", "Running"))));
            var holder = Doc("SW.Blocks.GlobalDB", "DB_Holder", Section("Static", Member("Conv", "\"FB_Conveyor\"") + Member("Flag", "Bool")));
            var viaHolder = Doc("SW.Blocks.FC", "FC_ReadsHolder", "", Network(Lad(Access("GlobalVariable", "DB_Holder", "Flag"))));
            var caller = Doc("SW.Blocks.OB", "Main", "", Network(Lad("<Call UId=\"30\"><CallInfo Name=\"FB_Conveyor\" BlockType=\"FB\" /></Call>")));
            var unrelated = Doc("SW.Blocks.FC", "FC_Unrelated", Section("Input", Member("Enable", "Bool")), Network(Lad(Access("LocalVariable", "Enable"))));

            var result = SimaticMl.RenameMembers(
                Docs(conveyor, instance, safety, holder, viaHolder, caller, unrelated), NoTags, "FB_Conveyor", Renames("Enable=Release"));

            CollectionAssert.AreEquivalent(new[] { "FB_Conveyor" }, result.Changed.ToList());
            CollectionAssert.AreEquivalent(
                new[] { "DB_Conv1", "F_Guard", "DB_Holder", "FC_ReadsHolder", "Main" }, result.Dependents.ToList());
        }

        [TestMethod]
        public void RenameMembers_WhatCannotBeResolved_IsReportedNotGuessed()
        {
            var db = Doc("SW.Blocks.GlobalDB", "DB_Line", Section("Static", Member("Speed", "Real")));
            var user = Doc("SW.Blocks.FC", "FC_User", "", Network(Lad(Access("GlobalVariable", "DB_NotExported", "Speed"))));

            var result = SimaticMl.RenameMembers(Docs(db, user), NoTags, "DB_Line", Renames("Speed=Velocity"));

            CollectionAssert.AreEqual(new[] { "DB_NotExported.Speed" }, Operands(user));
            Assert.AreEqual(1, result.Warnings.Count);
            StringAssert.Contains(result.Warnings[0], "\"DB_NotExported\".Speed");
            CollectionAssert.AreEquivalent(new[] { "DB_Line" }, result.Changed.ToList());
        }

        [TestMethod]
        public void RenameMembers_GraphActionText_IsRewritten()
        {
            var db = Doc("SW.Blocks.GlobalDB", "DB_Line", Section("Static", Member("Speed", "Real") + Member("Go", "Bool")));
            var sequence = Doc("SW.Blocks.FB", "FB_Sequence", Section("Static", Member("Go", "Bool")), Network(
                $"<Graph xmlns=\"{GraphNs}\"><Sequence><Steps><Step Number=\"1\"><Actions>" +
                "<Action Qualifier=\"N\"><Token Text=\"&quot;DB_Line&quot;.Go\" /><Token Text=\"&#10;\" /></Action>" +
                "<Action Qualifier=\"D\"><Token Text=\"#Go\" /><Token Text=\",\" /><Token Text=\"T#5s\" /></Action>" +
                "</Actions></Step></Steps></Sequence></Graph>"));

            var result = SimaticMl.RenameMembers(Docs(db, sequence), NoTags, "DB_Line", Renames("Go=Start run"));

            CollectionAssert.AreEqual(
                new[] { "\"DB_Line\".\"Start run\"", "\n", "#Go", ",", "T#5s" },
                sequence.Descendants().Where(e => e.Name.LocalName == "Token").Select(e => (string)e.Attribute("Text")!).ToList());
            Assert.AreEqual(0, result.Warnings.Count);
        }

        [TestMethod]
        public void TypeName_ReadsUserTypesOnly()
        {
            Assert.AreEqual("Motor", SimaticMl.TypeName("\"Motor\""));
            Assert.AreEqual("Motor of X", SimaticMl.TypeName("Array[0..\"MAX\"] of \"Motor of X\""));
            Assert.IsNull(SimaticMl.TypeName("Array[0..3] of Real"));
            Assert.IsNull(SimaticMl.TypeName("TON_TIME"));
        }

        [TestMethod]
        public void ParseRenames_ReadsPairsAndRejectsAnythingElse()
        {
            var renames = McpServer.ParseRenames(" Speed = Velocity ;Config.Limit=Max; ");

            Assert.AreEqual(2, renames.Count);
            Assert.AreEqual("Velocity", renames["Speed"]);
            Assert.AreEqual("Max", renames["Config.Limit"]);
            Assert.ThrowsException<global::ModelContextProtocol.McpException>(() => McpServer.ParseRenames("Speed"));
        }

        #endregion

        #region plc paths of hmi tags

        [TestMethod]
        public void RenamePlcPath_RenamesWholeMemberSegmentsButNeverTheBlock()
        {
            var leaves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Speed"] = "Velocity", ["DB_Line"] = "X" };

            Assert.AreEqual("DB_Line.Config.Velocity", SimaticMl.RenamePlcPath("DB_Line.Config.Speed", leaves));
            Assert.AreEqual("\"DB_Line\".Drives[2].\"Velocity\"", SimaticMl.RenamePlcPath("\"DB_Line\".Drives[2].\"Speed\"", leaves));
            Assert.AreEqual("DB_Line.SpeedLimit", SimaticMl.RenamePlcPath("DB_Line.SpeedLimit", leaves));
        }

        #endregion

        #region reports

        [TestMethod]
        public void WriteReport_WritesTheWholeResultAsReadableJson()
        {
            var result = new BatchRenameResult();
            result.Items.Add(new BatchRenameItem { Kind = "tag", OldName = "iPump1_Run", NewName = "iPump2_Run", UsedByFailsafe = { "F_Guard" } });
            result.Warnings.Add("S" + (char)0x00E1 + "t " + (char)0x010D + ". 1");

            var path = McpServer.WriteReport("Test", result);

            try
            {
                var text = System.IO.File.ReadAllText(path);

                StringAssert.Contains(text, "\"OldName\": \"iPump1_Run\"");
                StringAssert.Contains(text, "F_Guard");
                StringAssert.Contains(text, result.Warnings[0], "Non-ASCII text must stay readable, not escaped");
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        #endregion

        #region batch rename order

        /// <summary>Carries a plan out on a name list the way the setters would, failing on a clash.</summary>
        private static List<string> Apply(List<string> names, IList<string> oldNames, IList<string> newNames)
        {
            var index = oldNames.Select(o => names.FindIndex(n => n == o)).ToList();

            foreach (var (item, name) in BatchRename.Plan(oldNames, newNames, names.ToList()))
            {
                Assert.IsFalse(names.Where((n, i) => i != index[item]).Contains(name, StringComparer.OrdinalIgnoreCase), $"'{name}' is still taken");
                names[index[item]] = name;
            }

            return names;
        }

        [TestMethod]
        public void BatchRename_SwapRotationAndChain_NeverHitATakenName()
        {
            CollectionAssert.AreEqual(
                new[] { "B", "A", "Other" },
                Apply(new List<string> { "A", "B", "Other" }, new[] { "A", "B" }, new[] { "B", "A" }));
            CollectionAssert.AreEqual(
                new[] { "B", "C", "A" },
                Apply(new List<string> { "A", "B", "C" }, new[] { "A", "B", "C" }, new[] { "B", "C", "A" }));
            CollectionAssert.AreEqual(
                new[] { "B", "C" },
                Apply(new List<string> { "A", "B" }, new[] { "A", "B" }, new[] { "B", "C" }));
            CollectionAssert.AreEqual(
                new[] { "a", "B" },
                Apply(new List<string> { "A", "B" }, new[] { "A" }, new[] { "a" }));
        }

        [TestMethod]
        public void BatchRename_CollisionOutsideTheBatchOrDuplicate_Throws()
        {
            var names = new[] { "A", "B", "Other" };

            Assert.ThrowsException<PortalException>(() => BatchRename.Plan(new[] { "A" }, new[] { "other" }, names));
            Assert.ThrowsException<PortalException>(() => BatchRename.Plan(new[] { "A", "B" }, new[] { "X", "x" }, names));
            Assert.ThrowsException<PortalException>(() => BatchRename.Plan(new[] { "A", "a" }, new[] { "X", "Y" }, names));
            Assert.AreEqual(0, BatchRename.Plan(new[] { "A" }, new[] { "A" }, names).Count);
        }

        #endregion

        #region names with a slash

        private sealed class Node
        {
            public string Name = string.Empty;
            public List<Node> Groups = new List<Node>();
            public List<string> Tables = new List<string>();
        }

        [TestMethod]
        public void PathMatch_FindsTablesWhoseNameContainsASlash()
        {
            // root: table "IO" ; group "Station/1": tables "In/Out", "In"
            var root = new Node
            {
                Tables = { "IO" },
                Groups = { new Node { Name = "Station/1", Tables = { "In/Out", "In" } } }
            };

            List<(string Item, string Tail)> Find(string path) =>
                PathMatch.ByPrefix<Node, string>(root, path, g => g.Tables, g => g.Groups, t => t, g => g.Name).ToList();

            CollectionAssert.AreEqual(new[] { ("In/Out", ""), ("In", "Out") }, Find("Station/1/In/Out"));
            CollectionAssert.AreEqual(new[] { ("In/Out", "Tag/1"), ("In", "out/Tag/1") }, Find("station/1/in/out/Tag/1"));
            CollectionAssert.AreEqual(new[] { ("IO", "") }, Find("IO"));
            Assert.AreEqual(0, Find("IOX").Count, "A name is matched whole, never as a text prefix");
        }

        #endregion
    }
}
