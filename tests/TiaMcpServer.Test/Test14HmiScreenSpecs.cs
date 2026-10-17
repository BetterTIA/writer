using System;
using System.Drawing;
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

        private const string Button = "[{\"type\":\"button\",\"name\":\"btnStart\",\"left\":10,\"top\":20,\"width\":120,\"height\":40,\"text\":\"Start\"," +
            "\"properties\":{\"BackColor\":\"#00AA00\",\"Visible\":false},\"dynamizations\":{\"Visible\":\"Ready\"},\"events\":{\"Tapped\":\"Tags('Start').Write(1);\"}}]";

        [TestMethod]
        public void Test_1400_ParseItems_ReadsEveryField()
        {
            var items = HmiScreenSpecs.ParseItems(Button);

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("Button", items[0].Type, "the type is returned in its canonical casing");
            Assert.AreEqual("btnStart", items[0].Name);
            Assert.AreEqual(10, items[0].Left);
            Assert.AreEqual(120u, items[0].Width);
            Assert.AreEqual("Start", items[0].Text);
            Assert.AreEqual("#00AA00", items[0].PropertyValues["BackColor"]);
            Assert.AreEqual("false", items[0].PropertyValues["Visible"], "a non-string value is kept as its JSON text");
            Assert.AreEqual("Ready", items[0].Dynamizations!["Visible"]);
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
                "[{\"type\":\"Slider\",\"name\":\"a\",\"width\":1,\"height\":1}]",
                "[{\"type\":\"Button\",\"name\":\"a.b\",\"width\":1,\"height\":1}]",
                "[{\"type\":\"Button\",\"name\":\"a\",\"width\":0,\"height\":1}]",
                "[{\"type\":\"Button\",\"name\":\"a\",\"width\":1,\"height\":1,\"colour\":\"red\"}]",
                "[{\"type\":\"Button\",\"name\":\"a\",\"width\":1,\"height\":1},{\"type\":\"Text\",\"name\":\"A\",\"width\":1,\"height\":1}]"
            })
            {
                var ex = Assert.ThrowsException<PortalException>(() => HmiScreenSpecs.ParseItems(bad), bad);
                Assert.AreEqual(PortalErrorCode.InvalidParams, ex.Code, bad);
            }
        }

        [TestMethod]
        public void Test_1402_ParseItems_ACircleNeedsOnlyItsDiameter()
        {
            var items = HmiScreenSpecs.ParseItems("[{\"type\":\"Circle\",\"name\":\"lamp\",\"left\":5,\"top\":5,\"width\":30}]");

            Assert.AreEqual("Circle", items[0].Type);
            Assert.AreEqual(0u, items[0].Height);
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
            var emptyTag = HmiScreenSpecs.ParseItems("[{\"type\":\"Button\",\"name\":\"b\",\"width\":1,\"height\":1,\"dynamizations\":{\"Visible\":\" \"}}]")[0];
            var unknownEvent = HmiScreenSpecs.ParseItems("[{\"type\":\"Button\",\"name\":\"b\",\"width\":1,\"height\":1,\"events\":{\"Clicked\":\"x\"}}]")[0];
            var noneEvent = HmiScreenSpecs.ParseItems("[{\"type\":\"Button\",\"name\":\"b\",\"width\":1,\"height\":1,\"events\":{\"None\":\"x\"}}]")[0];
            var noEvents = HmiScreenSpecs.ParseItems("[{\"type\":\"Rectangle\",\"name\":\"r\",\"width\":1,\"height\":1,\"events\":{\"Tapped\":\"x\"}}]")[0];
            var emptyScript = HmiScreenSpecs.ParseItems("[{\"type\":\"Button\",\"name\":\"b\",\"width\":1,\"height\":1,\"events\":{\"Tapped\":\"\"}}]")[0];

            StringAssert.Contains(HmiScreenSpecs.Problem(text, typeof(FakeRectangle)), "no text");
            StringAssert.Contains(HmiScreenSpecs.Problem(property, typeof(FakeButton)), "no writable property 'Colour'");
            StringAssert.Contains(HmiScreenSpecs.Problem(readOnly, typeof(FakeButton)), "no writable property 'ReadOnly'");
            StringAssert.Contains(HmiScreenSpecs.Problem(reserved, typeof(FakeButton)), "geometry");
            StringAssert.Contains(HmiScreenSpecs.Problem(dynamization, typeof(FakeButton)), "no property 'Speed'");
            StringAssert.Contains(HmiScreenSpecs.Problem(emptyTag, typeof(FakeButton)), "names no tag");
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
    }
}
