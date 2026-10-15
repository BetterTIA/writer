using TiaMcpServer.Siemens;

namespace TiaMcpServer.Test
{
    /// <summary>
    /// Tests for the input rules of the hardware tools (HardwareRules). These tests do not
    /// connect to TIA Portal.
    /// </summary>
    [TestClass]
    public class Test10Hardware
    {
        [TestMethod]
        public void Test_1000_IsIpv4_AcceptsFourOctets()
        {
            Assert.IsTrue(HardwareRules.IsIpv4("192.168.0.1"));
            Assert.IsTrue(HardwareRules.IsIpv4("0.0.0.0"));
            Assert.IsTrue(HardwareRules.IsIpv4("255.255.255.255"));
        }

        [TestMethod]
        public void Test_1001_IsIpv4_RejectsEverythingElse()
        {
            Assert.IsFalse(HardwareRules.IsIpv4(""));
            Assert.IsFalse(HardwareRules.IsIpv4(null));
            Assert.IsFalse(HardwareRules.IsIpv4("192.168.0"));
            Assert.IsFalse(HardwareRules.IsIpv4("192.168.0.256"));
            Assert.IsFalse(HardwareRules.IsIpv4("192.168.0.1.5"));
            Assert.IsFalse(HardwareRules.IsIpv4("192.168.0.a"));
            Assert.IsFalse(HardwareRules.IsIpv4("192.168..1"));
            Assert.IsFalse(HardwareRules.IsIpv4(" 192.168.0.1"));
        }

        [TestMethod]
        public void Test_1002_IsSubnetMask_AcceptsContiguousOnes()
        {
            Assert.IsTrue(HardwareRules.IsSubnetMask("255.255.255.0"));
            Assert.IsTrue(HardwareRules.IsSubnetMask("255.255.0.0"));
            Assert.IsTrue(HardwareRules.IsSubnetMask("255.255.255.252"));
            Assert.IsTrue(HardwareRules.IsSubnetMask("255.255.255.255"));
            Assert.IsTrue(HardwareRules.IsSubnetMask("128.0.0.0"));
        }

        [TestMethod]
        public void Test_1003_IsSubnetMask_RejectsGaps()
        {
            Assert.IsFalse(HardwareRules.IsSubnetMask("255.255.0.255"));
            Assert.IsFalse(HardwareRules.IsSubnetMask("255.255.255.1"));
            Assert.IsFalse(HardwareRules.IsSubnetMask("0.0.0.0"));
            Assert.IsFalse(HardwareRules.IsSubnetMask("192.168.0.1"));
            Assert.IsFalse(HardwareRules.IsSubnetMask("mask"));
        }

        [TestMethod]
        public void Test_1004_PnDeviceName_AcceptsProfinetNames()
        {
            Assert.IsNull(HardwareRules.PnDeviceNameProblem("plc-1"));
            Assert.IsNull(HardwareRules.PnDeviceNameProblem("et200sp-station.line1"));
            Assert.IsNull(HardwareRules.PnDeviceNameProblem("io1"));
        }

        [TestMethod]
        public void Test_1005_PnDeviceName_NamesTheProblem()
        {
            Assert.IsNotNull(HardwareRules.PnDeviceNameProblem(""));
            Assert.IsNotNull(HardwareRules.PnDeviceNameProblem("PLC_1"), "uppercase and underscore");
            Assert.IsNotNull(HardwareRules.PnDeviceNameProblem("-plc"), "leading hyphen");
            Assert.IsNotNull(HardwareRules.PnDeviceNameProblem("plc-"), "trailing hyphen");
            Assert.IsNotNull(HardwareRules.PnDeviceNameProblem("plc..1"), "empty label");
            Assert.IsNotNull(HardwareRules.PnDeviceNameProblem("192.168.0.1"), "looks like an address");
            Assert.IsNotNull(HardwareRules.PnDeviceNameProblem("port-001"), "reserved prefix");
            Assert.IsNotNull(HardwareRules.PnDeviceNameProblem(new string('a', 64)), "label too long");
            Assert.IsNotNull(HardwareRules.PnDeviceNameProblem(string.Join(".", System.Linq.Enumerable.Repeat(new string('a', 60), 5))), "name too long");
        }

        [TestMethod]
        public void Test_1006_SubnetTypeIdentifier_MapsBothTypesIgnoringCase()
        {
            Assert.AreEqual(HardwareRules.EthernetTypeIdentifier, HardwareRules.SubnetTypeIdentifier("ethernet"));
            Assert.AreEqual(HardwareRules.ProfibusTypeIdentifier, HardwareRules.SubnetTypeIdentifier("PROFIBUS"));
        }

        [TestMethod]
        public void Test_1007_SubnetTypeIdentifier_RejectsOtherTypes()
        {
            var ex = Assert.ThrowsException<PortalException>(() => HardwareRules.SubnetTypeIdentifier("Mpi"));

            Assert.AreEqual(PortalErrorCode.InvalidParams, ex.Code);
            StringAssert.Contains(ex.Message, "Ethernet");
        }

        [TestMethod]
        public void Test_1008_IsFailsafeAttribute_UsesThePrefix()
        {
            Assert.IsTrue(HardwareRules.IsFailsafeAttribute("Failsafe_FDestinationAddress"));
            Assert.IsTrue(HardwareRules.IsFailsafeAttribute("Failsafe_CentralFSourceAddress"));
            Assert.IsFalse(HardwareRules.IsFailsafeAttribute("PnDeviceName"));
            Assert.IsFalse(HardwareRules.IsFailsafeAttribute("failsafe_x"), "the prefix is case-sensitive, as Openness attribute names are");
            Assert.IsFalse(HardwareRules.IsFailsafeAttribute(null));
        }
    }
}
