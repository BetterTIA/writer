using System.Linq;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.Test
{
    /// <summary>
    /// Tests for the batch parameters of the HMI write tools (HmiSpecs). These tests do not
    /// connect to TIA Portal.
    /// </summary>
    [TestClass]
    public class Test13HmiSpecs
    {
        [TestMethod]
        public void Test_1300_TagSpecs_BoundAndInternal()
        {
            var specs = HmiSpecs.ParseTagSpecs("Motor1_Speed=\"DB_Motors\".Motor1.Speed; Page:Int ; Flag = Tag_1");

            Assert.AreEqual(3, specs.Count);
            Assert.IsTrue(specs[0].Bound);
            Assert.AreEqual("Motor1_Speed", specs[0].Name);
            Assert.AreEqual("\"DB_Motors\".Motor1.Speed", specs[0].PlcTag);
            Assert.IsFalse(specs[1].Bound);
            Assert.AreEqual("Int", specs[1].DataType);
            Assert.AreEqual("Tag_1", specs[2].PlcTag, "spaces around the separator are trimmed");
        }

        [TestMethod]
        public void Test_1301_TagSpecs_RejectWhatIsNotATag()
        {
            foreach (var bad in new[] { "", "Motor1", "=Tag", "Name=", "A.B=Tag", "A=Tag; A:Int", "A:Int; a=Tag" })
            {
                var ex = Assert.ThrowsException<PortalException>(() => HmiSpecs.ParseTagSpecs(bad), bad);
                Assert.AreEqual(PortalErrorCode.InvalidParams, ex.Code);
            }
        }

        [TestMethod]
        public void Test_1302_AlarmSpecs_Discrete()
        {
            var specs = HmiSpecs.ParseAlarmSpecs("Pump1_Fault|Alarms.Word1|3|Pump 1 fault; Door|DoorTag; Lamp|Lamps|7", discrete: true);

            Assert.AreEqual(3, specs.Count);
            Assert.AreEqual("Alarms.Word1", specs[0].TriggerTag);
            Assert.AreEqual(3u, specs[0].BitNumber);
            Assert.AreEqual("Pump 1 fault", specs[0].EventText);
            Assert.AreEqual(0u, specs[1].BitNumber, "the bit defaults to 0");
            Assert.IsNull(specs[1].EventText);
            Assert.AreEqual(7u, specs[2].BitNumber);
        }

        [TestMethod]
        public void Test_1303_AlarmSpecs_Analog()
        {
            var specs = HmiSpecs.ParseAlarmSpecs("Temp_High|Temperature|upperlimit|80|Too hot; Level|Level_1|LowerLimitOrEqual|12.5", discrete: false);

            Assert.AreEqual("UpperLimit", specs[0].Condition, "the condition is returned in its canonical casing");
            Assert.AreEqual(80, specs[0].ConditionValue, "an integral value is an int for the setter");
            Assert.AreEqual("Too hot", specs[0].EventText);
            Assert.AreEqual(12.5, specs[1].ConditionValue);
            Assert.IsNull(specs[1].EventText);
        }

        [TestMethod]
        public void Test_1304_AlarmSpecs_RejectWhatIsNotAnAlarm()
        {
            foreach (var bad in new[] { "", "OnlyName", "A|Tag|x", "A|Tag|1|text|extra" })
            {
                Assert.ThrowsException<PortalException>(() => HmiSpecs.ParseAlarmSpecs(bad, discrete: true), "discrete " + bad);
            }

            foreach (var bad in new[] { "A|Tag", "A|Tag|Between|5", "A|Tag|Equal|five", "A|Tag|Equal|5; A|Tag|Equal|6" })
            {
                Assert.ThrowsException<PortalException>(() => HmiSpecs.ParseAlarmSpecs(bad, discrete: false), "analog " + bad);
            }
        }

        [TestMethod]
        public void Test_1305_NameProblem_FollowsTiaPortalsRules()
        {
            Assert.IsNull(HmiSpecs.NameProblem("Motor1_Speed"));
            Assert.IsNotNull(HmiSpecs.NameProblem(""));
            Assert.IsNotNull(HmiSpecs.NameProblem(" Motor"));
            Assert.IsNotNull(HmiSpecs.NameProblem("Motor.Speed"), "a dot separates members");
            Assert.IsNotNull(HmiSpecs.NameProblem("Mo\"tor"));
            Assert.IsNotNull(HmiSpecs.NameProblem("A;B"), "the batch separators cannot be part of a name");
        }

        [TestMethod]
        public void Test_1306_TextMarkup_WrapsPlainTextAndKeepsMarkup()
        {
            Assert.AreEqual("<body><p>Pump 1 &lt;stop&gt; &amp; go</p></body>", HmiSpecs.TextMarkup("Pump 1 <stop> & go"));
            Assert.AreEqual("<body><p>as is</p></body>", HmiSpecs.TextMarkup("<body><p>as is</p></body>"));
        }

        [TestMethod]
        public void Test_1307_TextMarkup_LineBreaksBecomeBrInOneParagraph()
        {
            // D7a: TIA Portal refuses <p>a</p><p>b</p>; only one paragraph with <br/> passes.
            Assert.AreEqual("<body><p>a<br/>b<br/>c<br/>d</p></body>", HmiSpecs.TextMarkup("a\nb\r\nc\rd"));
            Assert.AreEqual("<body><p></p></body>", HmiSpecs.TextMarkup(string.Empty));
            Assert.AreEqual("  <body><p>x</p></body>", HmiSpecs.TextMarkup("  <body><p>x</p></body>"), "markup is left alone");
        }

        [TestMethod]
        public void Test_1308_TextMarkupProblem_OneParagraphOnly()
        {
            Assert.IsNull(HmiSpecs.TextMarkupProblem("plain text\nwith a line"), "plain text is converted, never refused");
            Assert.IsNull(HmiSpecs.TextMarkupProblem("<body><p>a<br/>b</p></body>"));
            Assert.IsNull(HmiSpecs.TextMarkupProblem("<body><p><b>a</b>&nbsp;b &amp; c</p></body>"), "HTML entities do not fail the check");
            StringAssert.Contains(HmiSpecs.TextMarkupProblem("<body><p>a</p><p>b</p></body>"), "one <p>");
            StringAssert.Contains(HmiSpecs.TextMarkupProblem("<body>bare</body>"), "one <p>");
            StringAssert.Contains(HmiSpecs.TextMarkupProblem("<body><p>a</body>"), "not well-formed");
        }

        [TestMethod]
        public void Test_1390_DataTypeRefusal_OnlyBoundTags()
        {
            Assert.IsNotNull(HmiSpecs.DataTypeRefusal("PLC_1", "HmiDataType"));
            Assert.IsNotNull(HmiSpecs.DataTypeRefusal("PLC_1", "DataType"));
            Assert.IsNull(HmiSpecs.DataTypeRefusal("", "HmiDataType"));
            Assert.IsNull(HmiSpecs.DataTypeRefusal("<Internal tag>", "DataType"));
            Assert.IsNull(HmiSpecs.DataTypeRefusal("PLC_1", "InitialValue"));
        }
    }
}
