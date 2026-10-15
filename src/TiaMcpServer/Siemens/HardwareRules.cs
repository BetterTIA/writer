using System;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Input rules of the hardware tools (Portal.AnetFork.Hardware.cs) that need no TIA Portal:
    /// address formats, PROFINET device names, subnet types, and how a fail-safe module is told
    /// from a standard one. Pure, so Test10Hardware covers them without a project.
    /// </summary>
    public static class HardwareRules
    {
        public const string EthernetTypeIdentifier = "System:Subnet.Ethernet";
        public const string ProfibusTypeIdentifier = "System:Subnet.Profibus";

        /// <summary>
        /// Every F-parameter of a module or CPU is an attribute with this prefix
        /// (Failsafe_FSourceAddress, Failsafe_FDestinationAddress, Failsafe_CentralFSourceAddress, ...).
        /// </summary>
        public const string FailsafeAttributePrefix = "Failsafe_";

        public static bool IsFailsafeAttribute(string? attributeName) =>
            attributeName != null && attributeName.StartsWith(FailsafeAttributePrefix, StringComparison.Ordinal);

        /// <summary>'Ethernet' or 'Profibus' (any casing) to the Openness type identifier of the subnet.</summary>
        public static string SubnetTypeIdentifier(string networkType)
        {
            if (string.Equals(networkType, "Ethernet", StringComparison.OrdinalIgnoreCase))
            {
                return EthernetTypeIdentifier;
            }

            if (string.Equals(networkType, "Profibus", StringComparison.OrdinalIgnoreCase))
            {
                return ProfibusTypeIdentifier;
            }

            throw new PortalException(PortalErrorCode.InvalidParams,
                $"Unknown network type '{networkType}'. Use 'Ethernet' or 'Profibus'.");
        }

        /// <summary>Four decimal octets 0..255, nothing else.</summary>
        public static bool IsIpv4(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var octets = value!.Split('.');

            return octets.Length == 4 && octets.All(o => o.Length is > 0 and <= 3 && o.All(char.IsDigit) && int.Parse(o) <= 255);
        }

        /// <summary>An IPv4 address whose bits are ones followed by zeros, e.g. 255.255.255.0.</summary>
        public static bool IsSubnetMask(string? value)
        {
            if (!IsIpv4(value))
            {
                return false;
            }

            var bits = value!.Split('.').Select(int.Parse).Aggregate(0u, (acc, o) => (acc << 8) | (uint)o);

            // ones followed by zeros: inverting gives zeros followed by ones, which is one less than a power of two
            var inverted = ~bits;

            return bits != 0 && (inverted & (inverted + 1)) == 0;
        }

        /// <summary>
        /// Why <paramref name="name"/> is not a valid PROFINET device name, or null when it is.
        /// The rules of the PROFINET standard that TIA Portal checks as well: lowercase letters,
        /// digits, '-' and '.', labels of 1 to 63 characters that neither start nor end with '-',
        /// at most 240 characters, not of the form of an IP address, not starting with 'port-'.
        /// </summary>
        public static string? PnDeviceNameProblem(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "the name is empty";
            }

            if (name!.Length > 240)
            {
                return "the name is longer than 240 characters";
            }

            if (name.Any(c => !(c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '-' || c == '.')))
            {
                return "only lowercase letters a-z, digits, '-' and '.' are allowed";
            }

            foreach (var label in name.Split('.'))
            {
                if (label.Length == 0)
                {
                    return "a label between two dots is empty";
                }

                if (label.Length > 63)
                {
                    return $"the label '{label}' is longer than 63 characters";
                }

                if (label[0] == '-' || label[label.Length - 1] == '-')
                {
                    return $"the label '{label}' starts or ends with '-'";
                }
            }

            if (IsIpv4(name))
            {
                return "the name must not look like an IP address";
            }

            if (name.StartsWith("port-", StringComparison.Ordinal))
            {
                return "the name must not start with 'port-'";
            }

            return null;
        }
    }
}
