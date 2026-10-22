using System;
using System.Drawing;
using System.Linq;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.Test
{
    /// <summary>
    /// Tests for the item JSON and the rules of CreateHmiScreenItems (HmiScreenSpecs). The rules
    /// are checked against small stand-in types shaped like the Openness classes, so these tests
    /// do not need TIA Portal.
    /// </summary>
    [TestClass]
    public class Test14HmiScreenSpecs
    {
        private enum FakeEventType { None, Activated, Tapped }

        private sealed class FakeEventHandlers
        {
            public object Create(FakeEventType eventType) => eventType;
        }

        /// <summary>Shaped like HmiButton: text, events, a colour, an enum and a read-only property.</summary>
        private sealed class FakeButton
        {
            public string Name { get; set; } = string.Empty;
            public int Left { get; set; }
            public uint Width { get; set; }
            public object? Text { get; set; }
            public Color BackColor { get; set; }
            public bool Visible { get; set; }
            public DayOfWeek Mode { get; set; }
            public string ReadOnly { get; } = string.Empty;
            public FakeEventHandlers EventHandlers { get; } = new FakeEventHandlers();
        }

        /// <summary>Shaped like HmiRectangle: no text, no events.</summary>
        private sealed class FakeRectangle
        {
            public int Left { get; set; }
            public Color BackColor { get; set; }
        }

        /// <summary>Shaped like HmiFontPart: a part with its own writable properties.</summary>
        private sealed class FakeFont
        {
            public float Size { get; set; }
            public DayOfWeek Weight { get; set; }
        }

        /// <summary>Shaped like HmiLabel: a text with a font part.</summary>
        private sealed class FakeLabel
        {
            public int Left { get; set; }
            public uint Width { get; set; }
            public FakeFont Font { get; } = new FakeFont();
        }

        private sealed class FakeInterfaceEntry
        {
            public object? Value { get; set; }
        }

        /// <summary>Shaped like HmiFaceplateInterfaceComposition: entries found by name.</summary>
        private sealed class FakeInterface
        {
            public FakeInterfaceEntry? Find(string name) => null;
        }

        private sealed class FakeFaceplate
        {
            public FakeInterface Interface { get; } = new FakeInterface();
        }

        private sealed class FakeCircle
        {
            public int CenterX { get; set; }
            public uint Radius { get; set; }
        }

        private sealed class FakeEllipse
        {
            public int CenterX { get; set; }
            public uint RadiusX { get; set; }
        }

        private enum FakeToggleEventType { None, Toggled }

        private sealed class FakeToggleHandlers
        {
            public object Create(FakeToggleEventType eventType) => eventType;
        }

        private class FakeButtonBase
        {
            public FakeEventHandlers EventHandlers { get; } = new FakeEventHandlers();
        }

        /// <summary>Shaped like HmiToggleSwitch: it redeclares EventHandlers of its base HmiButton.</summary>
        private sealed class FakeToggle : FakeButtonBase
        {
            public new FakeToggleHandlers EventHandlers { get; } = new FakeToggleHandlers();
        }

        private const string Button = "[{\"type\":\"button\",\"name\":\"btnStart\",\"left\":10,\"top\":20,\"width\":120,\"height\":40,\"text\":\"Start\"," +
            "\"properties\":{\"BackColor\":\"#00AA00\",\"Visible\":false},\"dynamizations\":{\"Visible\":\"Ready\"},\"events\":{\"Tapped\":\"Tags('Start').Write(1);\"}}]";

        [TestMethod]
        public void Test_1400_ParseItems_ReadsEveryField()
        {
            var items = HmiScreenSpecs.ParseItems(Button);

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("button", items[0].Type, "the type is matched to an Openness class later, by the portal");
            Assert.AreEqual("btnStart", items[0].Name);
            Assert.AreEqual(10, items[0].Left);
            Assert.AreEqual(120u, items[0].Width);
            Assert.AreEqual("Start", items[0].Text);
            Assert.AreEqual("#00AA00", items[0].PropertyValues["BackColor"]);
            Assert.AreEqual("false", items[0].PropertyValues["Visible"], "a non-string value is kept as its JSON text");
            Assert.AreEqual("Ready", items[0].DynamizationSpecs["Visible"].Tag, "a plain string is a tag dynamization");
            Assert.AreEqual("Tags('Start').Write(1);", items[0].Events!["Tapped"], "a script with ';' and quotes survives");
        }

        [TestMethod]
        public void Test_1401_ParseItems_RejectsWhatIsNotAnItem()
        {
            foreach (var bad in new[]
            {
                "",
                "[]",
                "{\"type\":\"Button\"}",
                "[{\"type\":\"\",\"name\":\"a\",\"width\":1,\"height\":1}]",
                "[{\"type\":\"Button\",\"name\":\"a.b\",\"width\":1,\"height\":1}]",
                "[{\"type\":\"Faceplate\",\"name\":\"a\",\"width\":1,\"height\":1}]",
                "[{\"type\":\"Button\",\"name\":\"a\",\"faceplateType\":\"Motor\",\"width\":1,\"height\":1}]",
                "[{\"type\":\"Button\",\"name\":\"a\",\"width\":1,\"height\":1,\"dynamizations\":{\"Visible\":{\"tag\":\"X\",\"script\":\"return 1;\"}}}]",
                "[{\"type\":\"Button\",\"name\":\"b\",\"width\":1,\"height\":1,\"dynamizations\":{\"Visible\":\" \"}}]",
                "[{\"type\":\"Button\",\"name\":\"a\",\"width\":1,\"height\":1,\"colour\":\"red\"}]",
                "[{\"type\":\"Button\",\"name\":\"a\",\"width\":1,\"height\":1},{\"type\":\"Text\",\"name\":\"A\",\"width\":1,\"height\":1}]"
            })
            {
                var ex = Assert.ThrowsException<PortalException>(() => HmiScreenSpecs.ParseItems(bad), bad);
                Assert.AreEqual(PortalErrorCode.InvalidParams, ex.Code, bad);
            }
        }

        [TestMethod]
        public void Test_1402_Problem_ACircleNeedsOnlyItsDiameter()
        {
            var circle = HmiScreenSpecs.ParseItems("[{\"type\":\"Circle\",\"name\":\"lamp\",\"left\":5,\"top\":5,\"width\":30}]")[0];
            var flat = HmiScreenSpecs.ParseItems("[{\"type\":\"Rectangle\",\"name\":\"r\",\"width\":30}]")[0];

            Assert.IsNull(HmiScreenSpecs.Problem(circle, typeof(FakeCircle)));
            StringAssert.Contains(HmiScreenSpecs.Problem(flat, typeof(FakeRectangle)), "greater than 0");
        }

        [TestMethod]
        public void Test_1403_Problem_AcceptsWhatTheTypeHas()
        {
            var spec = HmiScreenSpecs.ParseItems(Button)[0];

            Assert.IsNull(HmiScreenSpecs.Problem(spec, typeof(FakeButton)));
        }

        [TestMethod]
        public void Test_1404_Problem_RefusesWhatTheTypeDoesNotHave()
        {
            var text = HmiScreenSpecs.ParseItems("[{\"type\":\"Rectangle\",\"name\":\"r\",\"width\":1,\"height\":1,\"text\":\"x\"}]")[0];
            var property = HmiScreenSpecs.ParseItems("[{\"type\":\"Button\",\"name\":\"b\",\"width\":1,\"height\":1,\"properties\":{\"Colour\":\"#000000\"}}]")[0];
            var readOnly = HmiScreenSpecs.ParseItems("[{\"type\":\"Button\",\"name\":\"b\",\"width\":1,\"height\":1,\"properties\":{\"ReadOnly\":\"x\"}}]")[0];
            var reserved = HmiScreenSpecs.ParseItems("[{\"type\":\"Button\",\"name\":\"b\",\"width\":1,\"height\":1,\"properties\":{\"left\":5}}]")[0];
            var dynamization = HmiScreenSpecs.ParseItems("[{\"type\":\"Button\",\"name\":\"b\",\"width\":1,\"height\":1,\"dynamizations\":{\"Speed\":\"Tag\"}}]")[0];
            var unknownEvent = HmiScreenSpecs.ParseItems("[{\"type\":\"Button\",\"name\":\"b\",\"width\":1,\"height\":1,\"events\":{\"Clicked\":\"x\"}}]")[0];
            var noneEvent = HmiScreenSpecs.ParseItems("[{\"type\":\"Button\",\"name\":\"b\",\"width\":1,\"height\":1,\"events\":{\"None\":\"x\"}}]")[0];
            var noEvents = HmiScreenSpecs.ParseItems("[{\"type\":\"Rectangle\",\"name\":\"r\",\"width\":1,\"height\":1,\"events\":{\"Tapped\":\"x\"}}]")[0];
            var emptyScript = HmiScreenSpecs.ParseItems("[{\"type\":\"Button\",\"name\":\"b\",\"width\":1,\"height\":1,\"events\":{\"Tapped\":\"\"}}]")[0];

            StringAssert.Contains(HmiScreenSpecs.Problem(text, typeof(FakeRectangle)), "no text");
            StringAssert.Contains(HmiScreenSpecs.Problem(property, typeof(FakeButton)), "no writable property 'Colour'");
            StringAssert.Contains(HmiScreenSpecs.Problem(readOnly, typeof(FakeButton)), "no writable property 'ReadOnly'");
            StringAssert.Contains(HmiScreenSpecs.Problem(reserved, typeof(FakeButton)), "geometry");
            StringAssert.Contains(HmiScreenSpecs.Problem(dynamization, typeof(FakeButton)), "no property 'Speed'");
            StringAssert.Contains(HmiScreenSpecs.Problem(unknownEvent, typeof(FakeButton)), "Activated, Tapped");
            StringAssert.Contains(HmiScreenSpecs.Problem(noneEvent, typeof(FakeButton)), "no event 'None'");
            StringAssert.Contains(HmiScreenSpecs.Problem(noEvents, typeof(FakeRectangle)), "no events");
            StringAssert.Contains(HmiScreenSpecs.Problem(emptyScript, typeof(FakeButton)), "no script");
        }

        [TestMethod]
        public void Test_1405_Property_FindsTheCanonicalName()
        {
            Assert.AreEqual("BackColor", HmiScreenSpecs.Property(typeof(FakeButton), "backcolor")!.Name);
            Assert.IsNull(HmiScreenSpecs.Property(typeof(FakeButton), "Radius"));
            Assert.AreEqual(typeof(FakeEventType), HmiScreenSpecs.EventType(typeof(FakeButton)));
            Assert.IsNull(HmiScreenSpecs.EventType(typeof(FakeRectangle)));
        }

        [TestMethod]
        public void Test_1406_Convert_TakesColoursEnumsNumbersAndBooleans()
        {
            Assert.AreEqual(Color.FromArgb(255, 0, 170, 0), HmiScreenSpecs.Convert(typeof(Color), "#00AA00"));
            Assert.AreEqual(Color.FromArgb(128, 255, 0, 0).ToArgb(), ((Color)HmiScreenSpecs.Convert(typeof(Color), "#80FF0000")).ToArgb());
            Assert.AreEqual(DayOfWeek.Friday, HmiScreenSpecs.Convert(typeof(DayOfWeek), "friday"));
            Assert.AreEqual(12u, HmiScreenSpecs.Convert(typeof(uint), " 12 "));
            Assert.AreEqual(1.5f, HmiScreenSpecs.Convert(typeof(float), "1.5"));
            Assert.AreEqual(false, HmiScreenSpecs.Convert(typeof(bool), "false"));
            Assert.ThrowsException<ArgumentException>(() => HmiScreenSpecs.Convert(typeof(DayOfWeek), "Someday"));
            Assert.ThrowsException<FormatException>(() => HmiScreenSpecs.Convert(typeof(uint), "wide"));
        }

        [TestMethod]
        public void Test_1407_PathProperty_WalksPartsAndCompositions()
        {
            Assert.AreEqual(typeof(float), HmiScreenSpecs.PathProperty(typeof(FakeLabel), "font.size")!.PropertyType, "a part, in any casing");
            Assert.AreEqual("Value", HmiScreenSpecs.PathProperty(typeof(FakeFaceplate), "Interface.Speed.Value")!.Name, "a composition takes the next segment as an entry name");
            Assert.IsNull(HmiScreenSpecs.PathProperty(typeof(FakeFaceplate), "Interface.Speed"), "an entry is no property");
            Assert.IsNull(HmiScreenSpecs.PathProperty(typeof(FakeLabel), "Font.Colour"));
            Assert.IsNull(HmiScreenSpecs.PathProperty(typeof(FakeLabel), "Border.Size"));

            var font = HmiScreenSpecs.ParseItems("[{\"type\":\"Label\",\"name\":\"l\",\"width\":1,\"height\":1,\"properties\":{\"Font.Size\":16,\"Font.Weight\":\"Friday\"},\"dynamizations\":{\"Font.Size\":\"Zoom\"}}]")[0];
            var readOnlyPart = HmiScreenSpecs.ParseItems("[{\"type\":\"Label\",\"name\":\"l\",\"width\":1,\"height\":1,\"properties\":{\"Font\":\"x\"}}]")[0];

            Assert.IsNull(HmiScreenSpecs.Problem(font, typeof(FakeLabel)));
            Assert.AreEqual("16", font.PropertyValues["Font.Size"]);
            StringAssert.Contains(HmiScreenSpecs.Problem(readOnlyPart, typeof(FakeLabel)), "no writable property 'Font'");
        }

        [TestMethod]
        public void Test_1408_ParseDynamization_TakesATagOrAScript()
        {
            var tag = HmiScreenSpecs.ParseDynamization("Speed", "d");
            var indirect = HmiScreenSpecs.ParseDynamization("{\"tag\":\"Pointer\",\"indirect\":true}", "d");
            var list = HmiScreenSpecs.ParseDynamization("{\"tag\":\"State\",\"resourceList\":\"StateTexts\"}", "d");
            var script = HmiScreenSpecs.ParseDynamization("{\"script\":\"return Tags('A').Read() ? 1 : 0;\",\"trigger\":\"T1s\"}", "d");

            Assert.AreEqual("Speed", tag.Tag);
            Assert.IsTrue(indirect.Indirect);
            Assert.AreEqual("tag State via StateTexts", list.ToString());
            Assert.AreEqual("script (T1s)", script.ToString());

            foreach (var bad in new[] { "", "{}", "{\"tag\":\"A\",\"trigger\":\"T1s\"}", "{\"script\":\"x\",\"indirect\":true}", "{\"tag\":\"A\",\"colour\":1}", "{\"tag\":" })
            {
                var ex = Assert.ThrowsException<PortalException>(() => HmiScreenSpecs.ParseDynamization(bad, "d"), bad);
                Assert.AreEqual(PortalErrorCode.InvalidParams, ex.Code, bad);
            }
        }

        [TestMethod]
        public void Test_1409_Geometry_FollowsTheShapeOfTheType()
        {
            var spec = new HmiItemSpec { Left = 10, Top = 20, Width = 40, Height = 30 };

            CollectionAssert.AreEquivalent(new[] { "Left", "Top", "Width", "Height" }, HmiScreenSpecs.Geometry(spec, typeof(FakeButton)).Keys.ToArray());
            Assert.AreEqual("20", HmiScreenSpecs.Geometry(spec, typeof(FakeCircle))["Radius"]);
            Assert.AreEqual("30", HmiScreenSpecs.Geometry(spec, typeof(FakeCircle))["CenterX"]);
            Assert.AreEqual("15", HmiScreenSpecs.Geometry(spec, typeof(FakeEllipse))["RadiusY"]);
            Assert.AreEqual("35", HmiScreenSpecs.Geometry(spec, typeof(FakeEllipse))["CenterY"]);
        }

        [TestMethod]
        public void Test_1410_Events_OfTheMostDerivedTypeAndOfAScreen()
        {
            Assert.AreEqual(typeof(FakeToggleEventType), HmiScreenSpecs.EventType(typeof(FakeToggle)), "a redeclared EventHandlers wins");
            Assert.IsNull(HmiScreenSpecs.EventsProblem("screen", typeof(FakeButton), HmiScreenSpecs.ParseEvents("{\"Tapped\":\"x;\"}")));
            StringAssert.Contains(HmiScreenSpecs.EventsProblem("screen", typeof(FakeButton), HmiScreenSpecs.ParseEvents("{\"Loaded\":\"x\"}")), "no event 'Loaded'");
            Assert.IsNull(HmiScreenSpecs.ParseEvents(" "));
            Assert.ThrowsException<PortalException>(() => HmiScreenSpecs.ParseEvents("[1]"));
            Assert.AreEqual("42", HmiScreenSpecs.Convert(typeof(object), "42"), "an object-typed value keeps the text");
        }
    }
}
