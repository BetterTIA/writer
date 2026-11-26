using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.Compiler;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        #region plc software

        [McpServerTool(Name = "GetSoftwareInfo", Title = "Get software info", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Show name, description and attributes of one PLC software")]
        public static ResponseSoftwareInfo GetSoftwareInfo(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath)
        {
            try
            {
                var software = Portal.GetPlcSoftware(softwarePath);
                if (software is not null)
                {

                    var attributes = Helper.GetAttributeList(software);

                    return new ResponseSoftwareInfo
                    {
                        Message = $"Software info retrieved from '{softwarePath}'",
                        Name = software.Name,
                        Attributes = attributes,
                        Description = software.ToString(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Software not found at '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving software info from '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CompileSoftware", Title = "Compile software", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Compile the PLC software and report every compiler message with the object it belongs to, so errors can be fixed without re-reading the whole PLC. Warnings are reported as a successful compile with detail; only errors fail the call. " +
                     "This is the recovery when CompileBlock leaves blocks inconsistent. Openness offers no 'rebuild all': blocks still inconsistent afterwards are listed (StillInconsistent) and then need Compile > Software (rebuild all) in TIA Portal by hand. " +
                     "The safety program is compiled with it when fail-safe blocks need it, which needs the safety password: the fail-safe blocks inconsistent before the compile are reported (FailsafeInconsistent)")]
        public static ResponseCompileSoftware CompileSoftware(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("password: the password of the safety program, needed only when the compile covers it; default: no password")] string password = "")
        {
            try
            {
                var (safetyProgram, loggedOn, failsafeInconsistent) = Portal.SafetyBeforeCompile(softwarePath);
                var result = Portal.CompileSoftware(softwarePath, password)
                    ?? throw new McpException($"Failed compiling software '{softwarePath}': no project is open.");
                var stillInconsistent = Portal.InconsistentBlocks(softwarePath);

                var messages = FlattenCompilerMessages(result.Messages, 0).ToList();
                var state = result.State.ToString();
                var failed = result.State == CompilerResultState.Error;
                var notes = CompileNotes(safetyProgram, loggedOn || !string.IsNullOrEmpty(password), failsafeInconsistent, stillInconsistent, failed);

                var response = new ResponseCompileSoftware
                {
                    Message = $"Compiling '{softwarePath}' finished with state '{state}': " +
                              $"{result.ErrorCount} error(s), {result.WarningCount} warning(s)" + notes,
                    State = state,
                    ErrorCount = result.ErrorCount,
                    WarningCount = result.WarningCount,
                    Messages = messages,
                    SafetyProgram = safetyProgram,
                    FailsafeInconsistent = failsafeInconsistent,
                    StillInconsistent = stillInconsistent,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = !failed,
                        ["state"] = state,
                        ["errorCount"] = result.ErrorCount,
                        ["warningCount"] = result.WarningCount,
                        ["messageCount"] = messages.Count,
                        ["safetyProgram"] = safetyProgram,
                        ["failsafeInconsistent"] = failsafeInconsistent.Count,
                        ["stillInconsistent"] = stillInconsistent.Count
                    }
                };

                if (failed)
                {
                    // The message text is what a client surfaces, so name the first few offenders
                    // rather than making the caller re-query to find out what broke.
                    var offenders = messages
                        .Where(m => string.Equals(m.State, nameof(CompilerResultState.Error), StringComparison.OrdinalIgnoreCase))
                        .Take(5)
                        .Select(m => $"{m.Path}: {m.Description}");

                    throw new McpException(
                        $"Compiling '{softwarePath}' failed with {result.ErrorCount} error(s). " +
                        string.Join(" | ", offenders) + notes);
                }

                return response;
            }
            catch (PortalException pex)
            {
                throw new McpException(TiaMcpServer.Siemens.ErrorDetail.Append(pex.Message, pex), pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error compiling software '{softwarePath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// The safety and rebuild part of the CompileSoftware message: what the compile covered of
        /// the safety program, and what stays inconsistent - which only a manual
        /// 'Software (rebuild all)' in TIA Portal can still fix, since Openness has none.
        /// </summary>
        /// <param name="canCompileSafety">Logged on to the safety program before, or a password was given.</param>
        public static string CompileNotes(bool safetyProgram, bool canCompileSafety, IList<string> failsafeInconsistent, IList<string> stillInconsistent, bool failed)
        {
            var notes = string.Empty;

            if (failsafeInconsistent.Count > 0)
            {
                notes += $". SAFETY: {failsafeInconsistent.Count} fail-safe block(s) were inconsistent before the compile " +
                         $"({List(failsafeInconsistent)}): the compile covers the safety program and changes its signature" +
                         (canCompileSafety ? string.Empty : "; TIA Portal compiles it only with the safety password ('password') or after a login in TIA Portal");
            }
            else if (safetyProgram)
            {
                notes += ". The PLC has a safety program; no fail-safe block was inconsistent before the compile";
            }

            if (stillInconsistent.Count > 0)
            {
                notes += $". WARNING: still inconsistent after the compile ({stillInconsistent.Count}): {List(stillInconsistent)} - " +
                         "Openness offers no 'rebuild all'; run Compile > Software (rebuild all) in TIA Portal";
            }

            if (failed && safetyProgram && !canCompileSafety)
            {
                notes += ". If the errors concern the safety program, pass 'password' or log on in TIA Portal";
            }

            return notes.Length == 0 ? notes : notes + ".";

            static string List(IList<string> paths) => string.Join(", ", paths.Take(15)) + (paths.Count > 15 ? ", ..." : string.Empty);
        }

        /// <summary>
        /// Walks the recursive CompilerResultMessage tree depth-first. Openness nests messages
        /// per object and then per detail, and the previous implementation discarded all of it
        /// in favour of CompilerResult.ToString().
        /// </summary>
        private static IEnumerable<CompileMessage> FlattenCompilerMessages(CompilerResultMessageComposition? messages, int depth)
        {
            if (messages == null)
            {
                yield break;
            }

            foreach (var message in messages)
            {
                if (message == null)
                {
                    continue;
                }

                yield return new CompileMessage
                {
                    Path = message.Path,
                    State = message.State.ToString(),
                    Description = message.Description,
                    ErrorCount = message.ErrorCount,
                    WarningCount = message.WarningCount,
                    Depth = depth
                };

                foreach (var child in FlattenCompilerMessages(message.Messages, depth + 1))
                {
                    yield return child;
                }
            }
        }

        #endregion
    }
}
