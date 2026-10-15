using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    /// <summary>One node (one address on one interface) of a device, as the hardware tools report it.</summary>
    public sealed class HardwareNode
    {
        /// <summary>Name of the device item that carries the interface, e.g. the CPU or the interface module.</summary>
        public string Item { get; set; } = string.Empty;

        /// <summary>The identity the node is selected by; it comes from a previous read (the Reader's read_hardware_config).</summary>
        public string NodeId { get; set; } = string.Empty;

        public string? Name { get; set; }

        public string? Address { get; set; }

        public string? SubnetMask { get; set; }

        public string? PnDeviceName { get; set; }

        /// <summary>True while TIA Portal derives the PROFINET name from the device name; PnDeviceName is read-only then.</summary>
        public bool? PnDeviceNameAutoGeneration { get; set; }

        /// <summary>Name of the connected subnet, or null.</summary>
        public string? Subnet { get; set; }

        public string? SubnetId { get; set; }

        /// <summary>Number of the IO system the interface's IO connector is attached to, or null.</summary>
        public int? IoSystem { get; set; }
    }

    public sealed class DeviceAddResult
    {
        public bool Applied { get; set; }

        public string DeviceName { get; set; } = string.Empty;

        public string TypeIdentifier { get; set; } = string.Empty;

        public string? RootItemName { get; set; }

        /// <summary>The nodes of the new device, with the NodeId that ConfigureNode takes.</summary>
        public List<HardwareNode> Nodes { get; set; } = new List<HardwareNode>();

        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class NodeConfigureResult
    {
        public bool Applied { get; set; }

        public string DeviceName { get; set; } = string.Empty;

        public HardwareNode Before { get; set; } = new HardwareNode();

        /// <summary>Setting name to requested value, in the order they are applied.</summary>
        public List<KeyValuePair<string, string>> Requested { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>After a real run: the node read back.</summary>
        public HardwareNode? After { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class SubnetResult
    {
        public bool Applied { get; set; }

        public string? SubnetId { get; set; }

        public string? Name { get; set; }

        public string? TypeIdentifier { get; set; }

        public int NodeCount { get; set; }

        public List<int> IoSystems { get; set; } = new List<int>();

        public List<string> Warnings { get; set; } = new List<string>();
    }

    public partial class Portal
    {
        // ANet fork additions (branch 'anet'): hardware and network writes, carried over from the
        // BetterTIA Reader (NetworkDeviceCreator, NetworkDeviceConfigurator, SubnetLifecycleService)
        // so that the Writer stays the only tool that changes a project. Reference for the contract:
        // Czarnak/tia-portal-mcp network_write (MIT), its live findings in PR #107 and #110.
        //
        // Callers: the tools in McpServer.AnetFork.Hardware.cs, registered only under
        // '--allow-write' and run inside one TIA Portal transaction each (McpServer.Guarded).
        // Affected API: none existing - all members are new. No file I/O.
        //
        // Every selector names exactly one object: a device by name, a node by the NodeId a
        // previous read reported, a subnet by its SubnetId, an IO system by its number on that
        // subnet. Zero or several matches fail; there is no first-match or name-only fallback.
        // Fail-safe hardware (a device item with Failsafe_* attributes) is refused: its network
        // settings stay with the operator.

        #region hardware: lookup

        private ProjectBase RequireProject() =>
            _project ?? throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal.");

        private static T? Safe<T>(Func<T> read)
        {
            try
            {
                return read();
            }
            catch (Exception)
            {
                // A property Openness refuses for this object; reported as unknown, never guessed.
                return default;
            }
        }

        private static string? Attribute(IEngineeringObject target, string name) =>
            Safe(() => target.GetAttribute(name)?.ToString());

        private static bool? AttributeBool(IEngineeringObject target, string name) =>
            Safe(() => target.GetAttribute(name) is bool value ? value : (bool?)null);

        /// <summary>Exactly one device by name, root devices and device groups included.</summary>
        private Device FindDeviceByName(string deviceName)
        {
            var devices = GetDevices();
            var matches = devices.Where(d => string.Equals(Safe(() => d.Name), deviceName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matches.Count == 1)
            {
                return matches[0];
            }

            if (matches.Count > 1)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"{matches.Count} devices are named '{deviceName}'; a device name must select exactly one device.");
            }

            var known = devices.Select(d => $"'{Safe(() => d.Name)}' ({RootItemName(d) ?? "-"})").ToList();

            throw new PortalException(PortalErrorCode.NotFound,
                $"No device named '{deviceName}'. The name is the station (root) name, not the CPU name. Devices: {string.Join(", ", known)}.");
        }

        private static string? RootItemName(Device device) =>
            Safe(() => device.DeviceItems.Select(i => i.Name).FirstOrDefault());

        private static IEnumerable<(DeviceItem Item, NetworkInterface Interface, Node Node)> EnumerateNodes(HardwareObject container)
        {
            foreach (DeviceItem item in container.DeviceItems)
            {
                var networkInterface = Safe(() => item.GetService<NetworkInterface>());

                if (networkInterface != null)
                {
                    foreach (Node node in networkInterface.Nodes)
                    {
                        yield return (item, networkInterface, node);
                    }
                }

                foreach (var nested in EnumerateNodes(item))
                {
                    yield return nested;
                }
            }
        }

        /// <summary>Exactly one node by NodeId across every interface of every item of the device.</summary>
        private static (DeviceItem Item, NetworkInterface Interface, Node Node) FindNode(Device device, string deviceName, string nodeId)
        {
            var matches = EnumerateNodes(device)
                .Where(n => string.Equals(Safe(() => n.Node.NodeId), nodeId, StringComparison.Ordinal))
                .ToList();

            if (matches.Count == 1)
            {
                return matches[0];
            }

            if (matches.Count > 1)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"Device '{deviceName}': {matches.Count} nodes report NodeId '{nodeId}'; the NodeId must select exactly one node.");
            }

            var known = EnumerateNodes(device).Select(n => $"{n.Item.Name}: {Safe(() => n.Node.NodeId)} ({Attribute(n.Node, "Address") ?? "-"})").ToList();

            throw new PortalException(PortalErrorCode.NotFound,
                $"Device '{deviceName}': no node with NodeId '{nodeId}'. Nodes: {string.Join(", ", known)}.");
        }

        private static HardwareNode ReadNode(DeviceItem item, NetworkInterface networkInterface, Node node)
        {
            var info = new HardwareNode
            {
                Item = item.Name,
                NodeId = Safe(() => node.NodeId) ?? string.Empty,
                Name = Safe(() => node.Name),
                Address = Attribute(node, "Address"),
                SubnetMask = Attribute(node, "SubnetMask"),
                PnDeviceName = Attribute(node, "PnDeviceName"),
                PnDeviceNameAutoGeneration = AttributeBool(node, "PnDeviceNameAutoGeneration")
            };

            var subnet = Safe(() => node.ConnectedSubnet);

            if (subnet != null)
            {
                info.Subnet = Safe(() => subnet.Name);
                info.SubnetId = Attribute(subnet, "SubnetId");
            }

            info.IoSystem = Safe(() => networkInterface.IoConnectors.FirstOrDefault()?.ConnectedToIoSystem?.Number);

            return info;
        }

        /// <summary>Exactly one subnet by its SubnetId attribute (ordinal), never by name.</summary>
        private Subnet FindSubnet(string subnetId)
        {
            var subnets = RequireProject().Subnets.ToList();
            var matches = subnets.Where(s => string.Equals(Attribute(s, "SubnetId"), subnetId, StringComparison.Ordinal)).ToList();

            if (matches.Count == 1)
            {
                return matches[0];
            }

            if (matches.Count > 1)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"{matches.Count} subnets report SubnetId '{subnetId}'; the SubnetId must select exactly one subnet.");
            }

            var known = subnets.Select(s => $"'{Safe(() => s.Name)}' = {Attribute(s, "SubnetId") ?? "?"}").ToList();

            throw new PortalException(PortalErrorCode.NotFound,
                $"No subnet with SubnetId '{subnetId}'. Subnets: {string.Join(", ", known)}.");
        }

        private static SubnetResult ReadSubnet(Subnet subnet) => new SubnetResult
        {
            SubnetId = Attribute(subnet, "SubnetId"),
            Name = Safe(() => subnet.Name),
            TypeIdentifier = Safe(() => subnet.TypeIdentifier),
            NodeCount = Safe(() => subnet.Nodes.Count),
            IoSystems = Safe(() => subnet.IoSystems.Select(i => i.Number).ToList()) ?? new List<int>()
        };

        private void EnsureFreeSubnetName(string name, Subnet? self)
        {
            var taken = RequireProject().Subnets
                .Where(s => self == null || !s.Equals(self))
                .Any(s => string.Equals(Safe(() => s.Name), name, StringComparison.OrdinalIgnoreCase));

            if (taken)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"A subnet named '{name}' already exists.");
            }
        }

        #endregion

        #region hardware: fail-safe

        private static bool HasFailsafeAttributes(IEngineeringObject target) =>
            Safe(() => target.GetAttributeInfos().Any(a => HardwareRules.IsFailsafeAttribute(a.Name)));

        /// <summary>Names of every device item of the container (recursively) that carries F-parameters.</summary>
        private static List<string> FailsafeItems(HardwareObject container)
        {
            var result = new List<string>();

            foreach (DeviceItem item in container.DeviceItems)
            {
                if (HasFailsafeAttributes(item))
                {
                    result.Add(item.Name);
                }

                result.AddRange(FailsafeItems(item));
            }

            return result;
        }

        /// <summary>The item and its parent items up to the device: what a node setting belongs to.</summary>
        private static List<DeviceItem> OwnerChain(DeviceItem item)
        {
            var chain = new List<DeviceItem>();

            for (IEngineeringObject? current = item; current is DeviceItem deviceItem; current = deviceItem.Parent)
            {
                chain.Add(deviceItem);
            }

            return chain;
        }

        #endregion

        #region hardware: devices

        /// <summary>
        /// Creates a device (a station with its head module) from a catalog type identifier, as
        /// the Reader's search_equipment_catalog reports it. A device that turns out to carry
        /// F-parameters is removed again and refused.
        /// </summary>
        /// <param name="deviceItemName">Name of the head module; empty creates the device without naming it.</param>
        public DeviceAddResult AddDevice(string typeIdentifier, string deviceName, string deviceItemName, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(AddDevice), PortalErrorCode.CreateFailed,
                () =>
                {
                    var project = RequireProject();

                    if (string.IsNullOrWhiteSpace(typeIdentifier))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "typeIdentifier must not be empty; take it from the hardware catalog.");
                    }

                    EnsureValidName(deviceName);

                    var devices = GetDevices();

                    if (devices.Any(d => string.Equals(Safe(() => d.Name), deviceName, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"A device named '{deviceName}' already exists.");
                    }

                    if (!string.IsNullOrWhiteSpace(deviceItemName)
                        && devices.Any(d => string.Equals(RootItemName(d), deviceItemName, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"A device item named '{deviceItemName}' already exists.");
                    }

                    var result = new DeviceAddResult { DeviceName = deviceName, TypeIdentifier = typeIdentifier, RootItemName = deviceItemName };

                    if (dryRun)
                    {
                        return result;
                    }

                    Device device;

                    try
                    {
                        device = string.IsNullOrWhiteSpace(deviceItemName)
                            ? project.Devices.Create(typeIdentifier, deviceName)
                            : project.Devices.CreateWithItem(typeIdentifier, deviceItemName, deviceName);
                    }
                    catch (EngineeringException ex)
                    {
                        throw new PortalException(PortalErrorCode.CreateFailed,
                            $"TIA Portal could not create a device from '{typeIdentifier}': {ex.Message} Check the type identifier in the hardware catalog.", null, ex);
                    }

                    var failsafe = FailsafeItems(device);

                    if (failsafe.Count > 0)
                    {
                        // Removed again before the refusal, for the case that no transaction is wrapping this call.
                        Safe(() => { device.Delete(); return true; });

                        throw new PortalException(PortalErrorCode.NotSupported,
                            $"'{typeIdentifier}' is fail-safe hardware (F-parameters on {string.Join(", ", failsafe)}). Adding safety hardware stays with the operator; the device was not kept.");
                    }

                    result.Applied = true;
                    result.DeviceName = Safe(() => device.Name) ?? deviceName;
                    result.TypeIdentifier = Safe(() => device.TypeIdentifier) ?? typeIdentifier;
                    result.RootItemName = RootItemName(device);
                    result.Nodes = EnumerateNodes(device).Select(n => ReadNode(n.Item, n.Interface, n.Node)).ToList();

                    if (result.RootItemName == null)
                    {
                        result.Warnings.Add("The created device exposes no device item.");
                    }

                    return result;
                },
                ("typeIdentifier", typeIdentifier), ("deviceName", deviceName), ("dryRun", dryRun));
        }

        #endregion

        #region hardware: nodes

        /// <summary>
        /// Sets the address, subnet mask and PROFINET name of exactly one node, connects it to a
        /// subnet and its interface to an IO system. Every requested setting is applied, or the
        /// call throws and the transaction around it rolls the earlier settings back.
        /// </summary>
        /// <param name="nodeId">The NodeId a previous read reported for the node; never the first node of the device.</param>
        /// <param name="subnetId">SubnetId of the subnet to connect to; empty leaves the connection unchanged.</param>
        /// <param name="ioSystemNumber">Number of an IO system on the connected subnet; 0 leaves it unchanged.</param>
        public NodeConfigureResult ConfigureNode(
            string deviceName, string nodeId, string ipAddress, string subnetMask, string pnDeviceName, string subnetId, int ioSystemNumber, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(ConfigureNode), PortalErrorCode.InvalidParams,
                () =>
                {
                    var device = FindDeviceByName(deviceName);
                    var (item, networkInterface, node) = FindNode(device, deviceName, nodeId);
                    var result = new NodeConfigureResult { DeviceName = deviceName, Before = ReadNode(item, networkInterface, node) };
                    var engineeringNode = (IEngineeringObject)node;

                    // What a node setting belongs to: the interface's item and the items above it (CPU, interface module).
                    var owners = OwnerChain(item);
                    var failsafeOwners = owners.Where(HasFailsafeAttributes).Select(o => o.Name).ToList();

                    if (failsafeOwners.Count > 0)
                    {
                        throw new PortalException(PortalErrorCode.NotSupported,
                            $"Node '{nodeId}' of '{deviceName}' belongs to fail-safe hardware ({string.Join(", ", failsafeOwners)} carry F-parameters). " +
                            "Network settings of safety hardware stay with the operator; nothing was changed.");
                    }

                    var otherFailsafe = FailsafeItems(device).Except(owners.Select(o => o.Name)).ToList();

                    if (otherFailsafe.Count > 0)
                    {
                        result.Warnings.Add(
                            $"The device carries fail-safe modules ({string.Join(", ", otherFailsafe)}). Their F-parameters are not touched; " +
                            "a changed PROFINET name still takes the whole station off the bus until the name is assigned again.");
                    }

                    // 1. the attributes, in the order TIA Portal needs them
                    var settings = new List<(string Name, object Value)>();

                    if (!string.IsNullOrWhiteSpace(pnDeviceName))
                    {
                        var problem = HardwareRules.PnDeviceNameProblem(pnDeviceName);

                        if (problem != null)
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams, $"'{pnDeviceName}' is not a valid PROFINET device name: {problem}.");
                        }

                        if (result.Before.PnDeviceNameAutoGeneration == true)
                        {
                            // PnDeviceName is read-only while TIA Portal derives it from the device name (seen live, upstream PR #110).
                            settings.Add(("PnDeviceNameAutoGeneration", false));
                        }

                        settings.Add(("PnDeviceName", pnDeviceName));
                    }

                    if (!string.IsNullOrWhiteSpace(ipAddress))
                    {
                        if (!HardwareRules.IsIpv4(ipAddress))
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams, $"'{ipAddress}' is not an IPv4 address.");
                        }

                        settings.Add(("Address", ipAddress));
                    }

                    if (!string.IsNullOrWhiteSpace(subnetMask))
                    {
                        if (!HardwareRules.IsSubnetMask(subnetMask))
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams, $"'{subnetMask}' is not a subnet mask.");
                        }

                        settings.Add(("SubnetMask", subnetMask));
                    }

                    // 2. the subnet
                    Subnet? subnet = null;
                    var connect = false;

                    if (!string.IsNullOrWhiteSpace(subnetId))
                    {
                        subnet = FindSubnet(subnetId);
                        var current = Safe(() => node.ConnectedSubnet);
                        connect = current == null || !current.Equals(subnet);

                        if (connect)
                        {
                            settings.Add(("Subnet", $"{Safe(() => subnet.Name)} ({subnetId})" + (current == null ? string.Empty : $", replacing '{Safe(() => current.Name)}'")));
                        }
                        else
                        {
                            result.Warnings.Add($"The node is already connected to subnet '{Safe(() => subnet.Name)}'.");
                        }
                    }

                    // 3. the IO system, on the subnet connected after this call
                    IoSystem? ioSystem = null;
                    IoConnector? connector = null;

                    if (ioSystemNumber != 0)
                    {
                        var ioSubnet = subnet ?? Safe(() => node.ConnectedSubnet)
                            ?? throw new PortalException(PortalErrorCode.InvalidParams,
                                "An IO system was requested, but the node is not connected to a subnet and no subnetId was given.");

                        var systems = Safe(() => ioSubnet.IoSystems.Where(i => i.Number == ioSystemNumber).ToList()) ?? new List<IoSystem>();

                        if (systems.Count != 1)
                        {
                            var known = Safe(() => ioSubnet.IoSystems.Select(i => $"{i.Name} ({i.Number})").ToList()) ?? new List<string>();

                            throw new PortalException(PortalErrorCode.NotFound,
                                $"{systems.Count} IO systems with number {ioSystemNumber} on subnet '{Safe(() => ioSubnet.Name)}'; it must select exactly one. IO systems: {string.Join(", ", known)}.");
                        }

                        ioSystem = systems[0];

                        var connectors = Safe(() => networkInterface.IoConnectors.ToList()) ?? new List<IoConnector>();

                        if (connectors.Count != 1)
                        {
                            throw new PortalException(PortalErrorCode.NotSupported,
                                $"The interface of '{item.Name}' exposes {connectors.Count} IO connectors; exactly one is needed to attach an IO device.");
                        }

                        connector = connectors[0];
                        var attached = Safe(() => connector.ConnectedToIoSystem);

                        if (attached != null && attached.Equals(ioSystem))
                        {
                            result.Warnings.Add($"The interface is already attached to IO system {ioSystemNumber}.");
                            connector = null;
                        }
                        else
                        {
                            settings.Add(("IoSystem", $"{Safe(() => ioSystem.Name)} ({ioSystemNumber})"));
                        }
                    }

                    result.Requested = settings.Select(s => new KeyValuePair<string, string>(s.Name, Convert.ToString(s.Value, CultureInfo.InvariantCulture) ?? string.Empty)).ToList();

                    if (settings.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "No setting was requested, or every requested setting is already in place.");
                    }

                    if (dryRun)
                    {
                        return result;
                    }

                    foreach (var (name, value) in settings.Where(s => s.Name != "Subnet" && s.Name != "IoSystem"))
                    {
                        try
                        {
                            engineeringNode.SetAttribute(name, value);
                        }
                        catch (EngineeringException ex)
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams,
                                $"TIA Portal refused {name} = '{value}' on node '{nodeId}' of '{deviceName}': {ex.Message} The call stops here; inside a transaction the earlier settings of this call are rolled back.", null, ex);
                        }
                    }

                    if (connect && subnet != null)
                    {
                        try
                        {
                            if (Safe(() => node.ConnectedSubnet) != null)
                            {
                                node.DisconnectFromSubnet();
                            }

                            node.ConnectToSubnet(subnet);
                        }
                        catch (EngineeringException ex)
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams,
                                $"TIA Portal refused to connect node '{nodeId}' of '{deviceName}' to subnet '{subnetId}': {ex.Message}", null, ex);
                        }
                    }

                    if (connector != null && ioSystem != null)
                    {
                        try
                        {
                            if (Safe(() => connector.ConnectedToIoSystem) != null)
                            {
                                connector.DisconnectFromIoSystem();
                            }

                            connector.ConnectToIoSystem(ioSystem);
                        }
                        catch (EngineeringException ex)
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams,
                                $"TIA Portal refused to attach the interface of '{item.Name}' to IO system {ioSystemNumber}: {ex.Message}", null, ex);
                        }
                    }

                    result.After = ReadNode(item, networkInterface, node);
                    result.Applied = true;

                    // The fact next to the request: a setter that returned without error but did not stick is a failure.
                    foreach (var (name, value) in settings)
                    {
                        var actual = name switch
                        {
                            "Address" => result.After.Address,
                            "SubnetMask" => result.After.SubnetMask,
                            "PnDeviceName" => result.After.PnDeviceName,
                            "PnDeviceNameAutoGeneration" => result.After.PnDeviceNameAutoGeneration?.ToString(),
                            "Subnet" => result.After.SubnetId,
                            "IoSystem" => result.After.IoSystem?.ToString(CultureInfo.InvariantCulture),
                            _ => null
                        };
                        var expected = name switch
                        {
                            "Subnet" => subnetId,
                            "IoSystem" => ioSystemNumber.ToString(CultureInfo.InvariantCulture),
                            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
                        };

                        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new PortalException(PortalErrorCode.InvalidState,
                                $"{name} reads back as '{actual ?? "(unknown)"}' instead of '{expected}' after the write on node '{nodeId}' of '{deviceName}'. The call fails so the transaction rolls the change back; inspect the device in TIA Portal.");
                        }
                    }

                    return result;
                },
                ("deviceName", deviceName), ("nodeId", nodeId), ("dryRun", dryRun));
        }

        #endregion

        #region hardware: subnets

        public SubnetResult CreateSubnet(string name, string networkType, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(CreateSubnet), PortalErrorCode.CreateFailed,
                () =>
                {
                    var project = RequireProject();
                    var typeIdentifier = HardwareRules.SubnetTypeIdentifier(networkType);

                    EnsureValidName(name);
                    EnsureFreeSubnetName(name, null);

                    if (dryRun)
                    {
                        return new SubnetResult { Name = name, TypeIdentifier = typeIdentifier };
                    }

                    var subnet = project.Subnets.Create(typeIdentifier, name);
                    var result = ReadSubnet(subnet);

                    if (string.IsNullOrWhiteSpace(result.SubnetId))
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            $"The subnet '{name}' was created but reports no SubnetId; the call fails so the transaction rolls it back.");
                    }

                    result.Applied = true;

                    return result;
                },
                ("name", name), ("networkType", networkType), ("dryRun", dryRun));
        }

        /// <param name="newName">Empty keeps the name.</param>
        /// <param name="highestAddress">PROFIBUS only; 0 keeps the value.</param>
        /// <param name="transmissionSpeed">PROFIBUS only, a value of the Openness enum (e.g. 'Kbps1500'); empty keeps the value.</param>
        public SubnetResult UpdateSubnet(string subnetId, string newName, int highestAddress, string transmissionSpeed, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(UpdateSubnet), PortalErrorCode.RenameFailed,
                () =>
                {
                    var subnet = FindSubnet(subnetId);
                    var engineeringSubnet = (IEngineeringObject)subnet;
                    var result = ReadSubnet(subnet);
                    var profibus = string.Equals(result.TypeIdentifier, HardwareRules.ProfibusTypeIdentifier, StringComparison.Ordinal);
                    var changes = 0;

                    if ((highestAddress != 0 || !string.IsNullOrWhiteSpace(transmissionSpeed)) && !profibus)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"Subnet '{result.Name}' is not a PROFIBUS subnet; highestAddress and transmissionSpeed do not apply.");
                    }

                    object? speed = null;

                    if (!string.IsNullOrWhiteSpace(transmissionSpeed))
                    {
                        var current = Safe(() => engineeringSubnet.GetAttribute("TransmissionSpeed"))
                            ?? throw new PortalException(PortalErrorCode.NotSupported, "TransmissionSpeed cannot be read from the subnet, so its enum type is unknown.");

                        try
                        {
                            speed = Enum.Parse(current.GetType(), transmissionSpeed, ignoreCase: true);
                        }
                        catch (ArgumentException)
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams,
                                $"'{transmissionSpeed}' is not a transmission speed. Values: {string.Join(", ", Enum.GetNames(current.GetType()))}.");
                        }

                        changes++;
                    }

                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        EnsureValidName(newName);
                        EnsureFreeSubnetName(newName, subnet);
                        changes++;
                    }

                    if (highestAddress != 0)
                    {
                        changes++;
                    }

                    if (changes == 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "No change was requested: give newName, highestAddress or transmissionSpeed.");
                    }

                    if (dryRun)
                    {
                        return result;
                    }

                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        subnet.Name = newName;
                    }

                    if (highestAddress != 0)
                    {
                        engineeringSubnet.SetAttribute("HighestAddress", highestAddress);
                    }

                    if (speed != null)
                    {
                        engineeringSubnet.SetAttribute("TransmissionSpeed", speed);
                    }

                    var after = ReadSubnet(FindSubnet(subnetId));
                    after.Applied = true;

                    return after;
                },
                ("subnetId", subnetId), ("dryRun", dryRun));
        }

        /// <summary>Deletes a subnet. Connected nodes stay in the project and are disconnected; no device is deleted.</summary>
        public SubnetResult DeleteSubnet(string subnetId, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(DeleteSubnet), PortalErrorCode.DeleteFailed,
                () =>
                {
                    var subnet = FindSubnet(subnetId);
                    var result = ReadSubnet(subnet);
                    var devicesBefore = RequireProject().Devices.Count;

                    if (result.NodeCount > 0)
                    {
                        result.Warnings.Add($"{result.NodeCount} node(s) are connected to this subnet and lose their connection; the devices stay.");
                    }

                    if (dryRun)
                    {
                        return result;
                    }

                    subnet.Delete();

                    var remaining = RequireProject().Subnets.Count(s => string.Equals(Attribute(s, "SubnetId"), subnetId, StringComparison.Ordinal));

                    if (remaining != 0 || RequireProject().Devices.Count != devicesBefore)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            $"After deleting subnet '{subnetId}' the project still reports {remaining} subnet(s) with that id, or the device count changed. The call fails so the transaction rolls it back.");
                    }

                    result.Applied = true;

                    return result;
                },
                ("subnetId", subnetId), ("dryRun", dryRun));
        }

        #endregion
    }
}
