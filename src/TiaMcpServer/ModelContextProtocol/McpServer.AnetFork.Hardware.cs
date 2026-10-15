using ModelContextProtocol.Server;
using System.Collections.Generic;
using System.ComponentModel;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseDeviceAdded : ResponseMessage
    {
        public bool DryRun { get; set; }

        public bool Applied { get; set; }

        public string? DeviceName { get; set; }

        public string? TypeIdentifier { get; set; }

        public string? RootItemName { get; set; }

        /// <summary>After a real run: the nodes of the new device, with the NodeId that ConfigureNode takes.</summary>
        public IEnumerable<HardwareNode>? Nodes { get; set; }

        public IEnumerable<string>? Warnings { get; set; }
    }

    public class ResponseNodeConfigured : ResponseMessage
    {
        public bool DryRun { get; set; }

        public bool Applied { get; set; }

        public string? DeviceName { get; set; }

        /// <summary>The node before the change.</summary>
        public HardwareNode? Before { get; set; }

        /// <summary>Setting name and requested value, in the order they are applied.</summary>
        public IEnumerable<KeyValuePair<string, string>>? Requested { get; set; }

        /// <summary>After a real run: the node read back, every requested value verified.</summary>
        public HardwareNode? After { get; set; }

        public IEnumerable<string>? Warnings { get; set; }
    }

    public class ResponseSubnet : ResponseMessage
    {
        public bool DryRun { get; set; }

        public bool Applied { get; set; }

        /// <summary>The identity ConfigureNode, UpdateSubnet and DeleteSubnet take.</summary>
        public string? SubnetId { get; set; }

        public string? Name { get; set; }

        public string? TypeIdentifier { get; set; }

        public int NodeCount { get; set; }

        public IEnumerable<int>? IoSystems { get; set; }

        public IEnumerable<string>? Warnings { get; set; }
    }

    public static partial class McpServer
    {
        // ANet fork additions (branch 'anet'): the hardware and network write tools for
        // Portal.AnetFork.Hardware.cs. Registered by Program.BuildTools() only under
        // '--allow-write'; every call runs in one TIA Portal transaction (Guarded), so a refused
        // setting rolls the earlier settings of the same call back. All default to dryRun = true.

        #region hardware and network

        private const string HardwareCheckHint =
            "Verify the result with a fresh hardware read (the Reader's read_hardware_config) before building on it.";

        [WriteTool]
        [McpServerTool(Name = "AddDevice", Title = "Add device from catalog type", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Create a device (a station with its head module) from a hardware catalog type identifier, e.g. 'OrderNumber:6ES7 155-6AU01-0CN0/V4.2'. The identifier comes from the Reader's search_equipment_catalog; the Writer does not search the catalog. Defaults to a dry run that checks the names and creates nothing. A name already taken by a device is rejected. Fail-safe hardware (F-CPU, F-modules) is refused and not kept: adding safety hardware stays with the operator. The real run returns the nodes of the new device with the NodeId that ConfigureNode takes")]
        public static ResponseDeviceAdded AddDevice(
            [Description("typeIdentifier: catalog type identifier of the device, as the Reader's search_equipment_catalog reports it")] string typeIdentifier,
            [Description("deviceName: name of the new station (the root device name)")] string deviceName,
            [Description("deviceItemName: name of the head module (CPU or interface module); empty lets TIA Portal name it")] string deviceItemName = "",
            [Description("dryRun: true (default) only checks the names; false creates the device")] bool dryRun = true)
        {
            return Guarded(nameof(AddDevice), () =>
            {
                var result = Portal.AddDevice(typeIdentifier, deviceName, deviceItemName, dryRun);

                return new ResponseDeviceAdded
                {
                    DryRun = dryRun,
                    Applied = result.Applied,
                    DeviceName = result.DeviceName,
                    TypeIdentifier = result.TypeIdentifier,
                    RootItemName = result.RootItemName,
                    Nodes = result.Applied ? result.Nodes : null,
                    Warnings = result.Warnings,
                    Message = result.Applied
                        ? $"Device '{result.DeviceName}' created from '{result.TypeIdentifier}' with {result.Nodes.Count} node(s). {HardwareCheckHint} {SaveHint}"
                        : $"Dry run: device '{deviceName}' would be created from '{typeIdentifier}'. Nothing was changed; repeat with dryRun=false to create it. Whether the type identifier is valid is only known when TIA Portal creates the device.",
                    Meta = result.Applied ? OkMeta() : Ok()
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "ConfigureNode", Title = "Configure network node", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Set the IP address, subnet mask and PROFINET device name of exactly one node of a device, connect it to a subnet and attach its interface to an IO system. The node is named by deviceName (the station name) and the NodeId a previous read reported (the Reader's read_hardware_config, or AddDevice); the first node of a device is never taken. Defaults to a dry run that reports the node as it is and every setting that would change. A PROFINET name on a node whose name is auto-generated first switches the generation off. On fail-safe hardware (an F-CPU or an F-interface carrying the node) only the IP address and the subnet mask may be set; a PROFINET name, subnet or IO system there is refused and stays with the operator. Every requested setting must apply and read back, otherwise the call fails and the transaction rolls the earlier settings back")]
        public static ResponseNodeConfigured ConfigureNode(
            [Description("deviceName: name of the station (root device), as the hardware read reports it")] string deviceName,
            [Description("nodeId: NodeId of the node, from the hardware read; selects exactly one node")] string nodeId,
            [Description("ipAddress: new IPv4 address; empty keeps the current one")] string ipAddress = "",
            [Description("subnetMask: new subnet mask, e.g. 255.255.255.0; empty keeps the current one")] string subnetMask = "",
            [Description("pnDeviceName: new PROFINET device name (lowercase letters, digits, '-', '.'); empty keeps the current one")] string pnDeviceName = "",
            [Description("subnetId: SubnetId of the subnet to connect the node to (from the hardware read or CreateSubnet); empty keeps the connection")] string subnetId = "",
            [Description("ioSystemNumber: number of an IO system on the connected subnet to attach the interface to, e.g. 100; 0 keeps it")] int ioSystemNumber = 0,
            [Description("dryRun: true (default) only reports what would change; false writes the settings")] bool dryRun = true)
        {
            return Guarded(nameof(ConfigureNode), () =>
            {
                var result = Portal.ConfigureNode(deviceName, nodeId, ipAddress, subnetMask, pnDeviceName, subnetId, ioSystemNumber, dryRun);
                var names = string.Join(", ", System.Linq.Enumerable.Select(result.Requested, r => r.Key));

                return new ResponseNodeConfigured
                {
                    DryRun = dryRun,
                    Applied = result.Applied,
                    DeviceName = result.DeviceName,
                    Before = result.Before,
                    Requested = result.Requested,
                    After = result.After,
                    Warnings = result.Warnings,
                    Message = result.Applied
                        ? $"Node '{nodeId}' of '{deviceName}': {names} written and read back. A changed address or PROFINET name takes effect in the PLC only after a download. {HardwareCheckHint} {SaveHint}"
                        : $"Dry run: node '{nodeId}' of '{deviceName}' would get {names}. Nothing was changed; repeat with dryRun=false to apply.",
                    Meta = result.Applied ? OkMeta() : Ok()
                };
            });
        }

        private static ResponseSubnet SubnetResponse(SubnetResult result, bool dryRun, string done, string planned) => new ResponseSubnet
        {
            DryRun = dryRun,
            Applied = result.Applied,
            SubnetId = result.SubnetId,
            Name = result.Name,
            TypeIdentifier = result.TypeIdentifier,
            NodeCount = result.NodeCount,
            IoSystems = result.IoSystems,
            Warnings = result.Warnings,
            Message = result.Applied
                ? $"{done} {HardwareCheckHint} {SaveHint}"
                : $"Dry run: {planned} Nothing was changed; repeat with dryRun=false to apply.",
            Meta = result.Applied ? OkMeta() : Ok()
        };

        [WriteTool]
        [McpServerTool(Name = "CreateSubnet", Title = "Create subnet", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Create an Ethernet or PROFIBUS subnet. A name already taken by a subnet is rejected. Defaults to a dry run that checks the name and creates nothing; the real run returns the SubnetId that ConfigureNode takes to connect nodes")]
        public static ResponseSubnet CreateSubnet(
            [Description("name: name of the new subnet, e.g. PN/IE_1")] string name,
            [Description("networkType: 'Ethernet' (default) or 'Profibus'")] string networkType = "Ethernet",
            [Description("dryRun: true (default) only checks the name; false creates the subnet")] bool dryRun = true)
        {
            return Guarded(nameof(CreateSubnet), () =>
            {
                var result = Portal.CreateSubnet(name, networkType, dryRun);

                return SubnetResponse(result, dryRun,
                    $"Subnet '{result.Name}' created with SubnetId '{result.SubnetId}'.",
                    $"subnet '{name}' of type '{result.TypeIdentifier}' would be created.");
            });
        }

        [WriteTool]
        [McpServerTool(Name = "UpdateSubnet", Title = "Rename subnet or set PROFIBUS values", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Rename a subnet, or set the highest address and transmission speed of a PROFIBUS subnet. The subnet is named by its SubnetId from the hardware read; PROFIBUS values on an Ethernet subnet are rejected. Defaults to a dry run that reports the subnet as it is and changes nothing")]
        public static ResponseSubnet UpdateSubnet(
            [Description("subnetId: SubnetId of the subnet, from the hardware read or CreateSubnet")] string subnetId,
            [Description("newName: the new subnet name; empty keeps the current one")] string newName = "",
            [Description("highestAddress: PROFIBUS highest station address, e.g. 126; 0 keeps the current one")] int highestAddress = 0,
            [Description("transmissionSpeed: PROFIBUS transmission speed as the Openness enum names it; empty keeps the current one")] string transmissionSpeed = "",
            [Description("dryRun: true (default) only reports the subnet; false writes the change")] bool dryRun = true)
        {
            return Guarded(nameof(UpdateSubnet), () =>
            {
                var result = Portal.UpdateSubnet(subnetId, newName, highestAddress, transmissionSpeed, dryRun);

                return SubnetResponse(result, dryRun,
                    $"Subnet '{subnetId}' updated; it is now named '{result.Name}'.",
                    $"subnet '{result.Name}' ({subnetId}) would be changed.");
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteSubnet", Title = "Delete subnet", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete a subnet named by its SubnetId from the hardware read. Nodes connected to it lose their connection and stay in the project; no device is deleted. Defaults to a dry run that reports the subnet with its node and IO system count and deletes nothing")]
        public static ResponseSubnet DeleteSubnet(
            [Description("subnetId: SubnetId of the subnet, from the hardware read")] string subnetId,
            [Description("dryRun: true (default) only reports the subnet; false deletes it")] bool dryRun = true)
        {
            return Guarded(nameof(DeleteSubnet), () =>
            {
                var result = Portal.DeleteSubnet(subnetId, dryRun);

                return SubnetResponse(result, dryRun,
                    $"Subnet '{result.Name}' ({subnetId}) deleted; {result.NodeCount} node(s) were disconnected.",
                    $"subnet '{result.Name}' ({subnetId}) with {result.NodeCount} connected node(s) and {result.IoSystems.Count} IO system(s) would be deleted.");
            });
        }

        #endregion
    }
}
