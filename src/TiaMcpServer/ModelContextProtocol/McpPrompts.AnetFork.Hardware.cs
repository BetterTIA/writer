using ModelContextProtocol.Server;
using System.ComponentModel;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpPrompts
    {
        // ANet fork additions: one prompt per tool of McpServer.AnetFork.Hardware.cs, as Test_708
        // and Test_709 require.

        #region ANet fork hardware templates

        [McpServerPrompt(Name = "AddDevice"), Description("Create a device from a hardware catalog type")]
        public static string AddDevice(string typeIdentifier, string deviceName, string deviceItemName = "", string dryRun = "true")
        {
            return $@"Create a device (station with head module) from a catalog type identifier. Take the identifier from the Reader's search_equipment_catalog. Run it with dryRun true first, then repeat with dryRun false; the real run returns the nodes with their NodeId for ConfigureNode (requires the server started with '--allow-write').

Common parameter values:
- typeIdentifier: catalog type identifier, e.g. OrderNumber:6ES7 155-6AU01-0CN0/V4.2
- deviceName: name of the new station
- deviceItemName: name of the head module; empty lets TIA Portal name it
- dryRun: true only checks the names, false creates the device

Use the AddDevice tool with these parameters:
- typeIdentifier: {typeIdentifier}
- deviceName: {deviceName}
- deviceItemName: {deviceItemName}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "ConfigureNode"), Description("Set address, PROFINET name, subnet and IO system of one node")]
        public static string ConfigureNode(string deviceName, string nodeId, string ipAddress = "", string subnetMask = "", string pnDeviceName = "", string subnetId = "", string ioSystemNumber = "0", string dryRun = "true")
        {
            return $@"Configure exactly one network node of a device: address, subnet mask, PROFINET device name, subnet connection and IO system. Take deviceName and nodeId from a hardware read (the Reader's read_hardware_config) or from AddDevice. Run it with dryRun true first, show the reported settings, then repeat with dryRun false. On fail-safe hardware only the IP address and the subnet mask may be set (requires the server started with '--allow-write').

Common parameter values:
- deviceName: name of the station (root device)
- nodeId: NodeId of the node from the hardware read
- ipAddress: new IPv4 address, empty keeps it
- subnetMask: new mask, e.g. 255.255.255.0, empty keeps it
- pnDeviceName: new PROFINET device name, empty keeps it
- subnetId: SubnetId to connect to, empty keeps the connection
- ioSystemNumber: number of the IO system on that subnet, 0 keeps it
- dryRun: true only reports, false writes

Use the ConfigureNode tool with these parameters:
- deviceName: {deviceName}
- nodeId: {nodeId}
- ipAddress: {ipAddress}
- subnetMask: {subnetMask}
- pnDeviceName: {pnDeviceName}
- subnetId: {subnetId}
- ioSystemNumber: {ioSystemNumber}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "CreateSubnet"), Description("Create an Ethernet or PROFIBUS subnet")]
        public static string CreateSubnet(string name, string networkType = "Ethernet", string dryRun = "true")
        {
            return $@"Create a subnet. The real run returns the SubnetId that ConfigureNode takes to connect nodes (requires the server started with '--allow-write').

Common parameter values:
- name: name of the new subnet, e.g. PN/IE_1
- networkType: Ethernet or Profibus
- dryRun: true only checks the name, false creates the subnet

Use the CreateSubnet tool with these parameters:
- name: {name}
- networkType: {networkType}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "UpdateSubnet"), Description("Rename a subnet or set PROFIBUS address and speed")]
        public static string UpdateSubnet(string subnetId, string newName = "", string highestAddress = "0", string transmissionSpeed = "", string dryRun = "true")
        {
            return $@"Rename a subnet, or set the highest address and transmission speed of a PROFIBUS subnet. Take the SubnetId from a hardware read (requires the server started with '--allow-write').

Common parameter values:
- subnetId: SubnetId of the subnet
- newName: new name, empty keeps it
- highestAddress: PROFIBUS highest station address, 0 keeps it
- transmissionSpeed: PROFIBUS speed as the Openness enum names it, empty keeps it
- dryRun: true only reports, false writes

Use the UpdateSubnet tool with these parameters:
- subnetId: {subnetId}
- newName: {newName}
- highestAddress: {highestAddress}
- transmissionSpeed: {transmissionSpeed}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "DeleteSubnet"), Description("Delete a subnet; connected nodes stay in the project")]
        public static string DeleteSubnet(string subnetId, string dryRun = "true")
        {
            return $@"Delete a subnet named by its SubnetId. Connected nodes are disconnected and stay; no device is deleted. Run it with dryRun true first to see the node count (requires the server started with '--allow-write').

Common parameter values:
- subnetId: SubnetId of the subnet from a hardware read
- dryRun: true only reports, false deletes

Use the DeleteSubnet tool with these parameters:
- subnetId: {subnetId}
- dryRun: {dryRun}";
        }

        #endregion
    }
}
