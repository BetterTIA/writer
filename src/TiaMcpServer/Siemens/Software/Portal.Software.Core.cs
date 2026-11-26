using Microsoft.Extensions.Logging;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.WatchAndForceTables;
using Siemens.Engineering.Safety;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        #region software

        public PlcSoftware? GetPlcSoftware(string softwarePath)
        {
            _logger?.LogInformation($"Getting software by path: {softwarePath}");

            if (IsProjectNull())
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);

            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                return plcSoftware;
            }

            return null;
        }

        /// <summary>
        /// Compiles the PLC software with ICompilable.Compile(), the only compile Openness V21
        /// offers: there is no 'rebuild all' and no compile option (checked against the V21
        /// PublicAPI). A safety program is compiled with it when it needs compiling, which TIA
        /// Portal allows only after the login with the safety password - never bypassed here.
        /// </summary>
        /// <returns>The compiler result, or null without a project.</returns>
        /// <exception cref="PortalException">The path names no PLC software, the safety login failed or the compile threw.</exception>
        public CompilerResult? CompileSoftware(string softwarePath, string password = "")
        {
            _logger?.LogInformation($"Compiling software by path: {softwarePath}");

            if (IsProjectNull())
            {
                return null; // "Error, no project";
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);

            if (softwareContainer?.Software is not PlcSoftware plcSoftware)
            {
                throw new PortalException(PortalErrorCode.NotFound, $"No PLC software found at '{softwarePath}'. 'GetProjectTree' lists them.");
            }

            if (!string.IsNullOrEmpty(password))
            {
                var admin = (softwareContainer.Parent as DeviceItem)?.GetService<SafetyAdministration>();

                if (admin != null && !admin.IsLoggedOnToSafetyOfflineProgram)
                {
                    try
                    {
                        admin.LoginToSafetyOfflineProgram(new NetworkCredential("", password).SecurePassword);
                    }
                    catch (Exception ex)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"Login to the safety program of '{softwarePath}' failed ({ex.Message}); nothing was compiled. Check 'password'.", null, ex);
                    }
                }
            }

            try
            {
                return plcSoftware.GetService<ICompilable>().Compile();
            }
            catch (Exception ex)
            {
                throw new PortalException(PortalErrorCode.InvalidState, $"Compiling '{softwarePath}' threw: {ex.Message}", null, ex);
            }
        }

        /// <summary>
        /// What a compile of the whole PLC software will touch of the safety program, read before
        /// it runs: whether the PLC has one, whether the session is logged on to it, and the
        /// fail-safe blocks that are inconsistent now, plus the inconsistent standard blocks that
        /// fail-safe blocks use - what the compile compiles of the safety program, which changes
        /// it and its signature.
        /// </summary>
        public (bool SafetyProgram, bool LoggedOn, List<string> FailsafeInconsistent) SafetyBeforeCompile(string softwarePath)
        {
            return Operation.Run(_logger, nameof(SafetyBeforeCompile), PortalErrorCode.InvalidState,
                () =>
                {
                    LoadSafetyBlocks(softwarePath);

                    var loggedOn = false;

                    try
                    {
                        loggedOn = (GetSoftwareContainer(softwarePath)?.Parent as DeviceItem)?.GetService<SafetyAdministration>()?.IsLoggedOnToSafetyOfflineProgram ?? false;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Safety login state could not be read for {SoftwarePath}", softwarePath);
                    }

                    var blocks = GetBlocks(softwarePath);
                    var failsafe = blocks.Where(IsFailsafe).ToList();
                    var safetyProgram = _safetyBlocks.Count > 0 || failsafe.Count > 0;
                    var affected = failsafe.Where(b => !b.IsConsistent).Select(GetBlockPath).ToList();

                    if (safetyProgram)
                    {
                        // An inconsistent standard block the safety program uses (a DB it reads) makes the compile
                        // cover the safety program too - seen live: CompileBlock failed, only a rebuild helped.
                        var failsafeNames = new HashSet<string>(failsafe.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);

                        foreach (var block in blocks.Where(b => !b.IsConsistent && !IsFailsafe(b)))
                        {
                            try
                            {
                                var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                                foreach (var source in block.GetService<global::Siemens.Engineering.CrossReference.CrossReferenceService>()?
                                             .GetCrossReferences(global::Siemens.Engineering.CrossReference.CrossReferenceFilter.AllObjects).Sources.ToList()
                                         ?? new List<global::Siemens.Engineering.CrossReference.SourceObject>())
                                {
                                    CollectReferences(source, all, new HashSet<string>());
                                }

                                var users = all.Where(failsafeNames.Contains).ToList();

                                if (users.Count > 0)
                                {
                                    affected.Add($"{GetBlockPath(block)} (used by fail-safe {string.Join(", ", users)})");
                                }
                            }
                            catch (Exception ex)
                            {
                                // Not knowing must not read as "the safety program is not affected".
                                affected.Add($"{GetBlockPath(block)} (use by fail-safe blocks unknown: {ex.Message})");
                            }
                        }
                    }

                    return (safetyProgram, loggedOn, affected);
                },
                ("softwarePath", softwarePath));
        }

        /// <summary>Paths of the blocks that are inconsistent right now, fail-safe ones marked.</summary>
        public List<string> InconsistentBlocks(string softwarePath) =>
            Operation.Run(_logger, nameof(InconsistentBlocks), PortalErrorCode.InvalidState, () => Inconsistent(softwarePath), ("softwarePath", softwarePath));

        #endregion

        #region get software container ...

        private SoftwareContainer? GetSoftwareContainer(string softwarePath)
        {
            if (_project == null)
            {
                return null;
            }

            string[] pathSegments = softwarePath.Split('/');
            int index = 0;

            if (index >= pathSegments.Length)
                return null;

            string segment = pathSegments[index];
            SoftwareContainer? softwareContainer = null;

            // in Devices
            if (_project.Devices != null)
            {
                softwareContainer = GetSoftwareContainerInDevices(_project.Devices, pathSegments, index);
                if (softwareContainer is not null)
                {
                    return softwareContainer;
                }
            }

            // in Groups
            if (_project.DeviceGroups != null)
            {
                softwareContainer = GetSoftwareContainerInGroups(_project.DeviceGroups, pathSegments, index);
                if (softwareContainer is not null)
                {
                    return softwareContainer;
                }
            }

            return null;
        }

        private SoftwareContainer? GetSoftwareContainerInDevices(DeviceComposition devices, string[] pathSegments, int index)
        {

            if (index >= pathSegments.Length)
                return null;

            string segment = pathSegments[index];
            string nextSegment = index + 1 < pathSegments.Length ? pathSegments[index + 1] : string.Empty;

            if (devices != null)
            {
                SoftwareContainer? softwareContainer = null;
                Device? device = null;
                DeviceItem? deviceItem = null;

                // a pc based plc has a Device.Name = 'PC-System_1' or something like that, which is visible in the TIA-Portal IDE
                // use segment to find device
                device = devices.FirstOrDefault(d => d.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (device is not null)
                {
                    // then use next segment to find device item
                    deviceItem = device.DeviceItems.FirstOrDefault(di => di.Name.Equals(nextSegment, StringComparison.OrdinalIgnoreCase));
                    // but here we use next segment to find device item
                    softwareContainer = GetSoftwareContainerInDeviceItem(deviceItem, pathSegments, index + 1);
                    if (softwareContainer is not null)
                    {
                        return softwareContainer;
                    }
                }

                // a hardware plc has a Device.Name = 'S7-1500/ET200MP-Station_1' or something like that, which is not visible in the TIA-Portal IDE
                // ignored segment for Device.Name and use it for DeviceItem.Name
                deviceItem = devices
                    .SelectMany(d => d.DeviceItems)
                    .FirstOrDefault(di => di.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (deviceItem is not null)
                {
                    return GetSoftwareContainerInDeviceItem(deviceItem, pathSegments, index);
                }

            }

            return null;
        }

        private SoftwareContainer? GetSoftwareContainerInGroups(DeviceUserGroupComposition groups, string[] pathSegments, int index)
        {
            if (index >= pathSegments.Length)
                return null;

            string segment = pathSegments[index];
            SoftwareContainer? softwareContainer = null;

            if (groups != null)
            {
                var group = groups.FirstOrDefault(g => g.Name.Equals(segment));
                if (group is not null)
                {
                    // when segment matched
                    softwareContainer = GetSoftwareContainerInDevices(group.Devices, pathSegments, index + 1);
                    if (softwareContainer is not null)
                    {
                        return softwareContainer;
                    }

                    return GetSoftwareContainerInGroups(group.Groups, pathSegments, index + 1);
                }
            }

            return null;
        }

        private SoftwareContainer? GetSoftwareContainerInDeviceItem(DeviceItem deviceItem, string[] pathSegments, int index)
        {
            if (deviceItem != null)
            {
                // when segment matched
                if (index == pathSegments.Length - 1)
                {
                    // get from DeviceItem
                    var softwareContainer = deviceItem.GetService<SoftwareContainer>();
                    if (softwareContainer != null)
                    {
                        return softwareContainer;
                    }
                }
            }

            return null;
        }

        #endregion
    }
}
