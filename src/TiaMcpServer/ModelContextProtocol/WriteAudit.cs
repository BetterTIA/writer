using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// One JSON line per call of a write tool, appended to a file outside the repository: when,
    /// which tool, its arguments without block contents (strings are cut at 200 characters),
    /// whether it was a dry run, how it ended. The record is written for refused and failed
    /// calls as well, so the file tells what was attempted, not only what succeeded.
    ///
    /// Callers: Program.cs (Track for every [WriteTool] in BuildTools, Filter as a call-tool
    /// filter). File: %LOCALAPPDATA%\BetterTIA\Writer\audit\writer-audit-&lt;yyyy-MM&gt;.jsonl, or
    /// the directory in TIAMCP_AUDIT_DIR; append only. A record that cannot be written is logged
    /// and does not fail the call. Record is pure, so Test11Safeguards covers it.
    /// </summary>
    public static class WriteAudit
    {
        public const string DirectoryVariable = "TIAMCP_AUDIT_DIR";

        private const int MaxArgumentChars = 200;

        private const int MaxMessageChars = 500;

        private static readonly HashSet<string> _tracked = new HashSet<string>(StringComparer.Ordinal);

        public static void Track(string toolName)
        {
            lock (_tracked)
            {
                _tracked.Add(toolName);
            }
        }

        public static bool IsTracked(string? toolName)
        {
            lock (_tracked)
            {
                return toolName != null && _tracked.Contains(toolName);
            }
        }

        public static string Directory
        {
            get
            {
                var configured = Environment.GetEnvironmentVariable(DirectoryVariable);

                return string.IsNullOrWhiteSpace(configured)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterTIA", "Writer", "audit")
                    : configured!;
            }
        }

        public static string FilePath(DateTime time) => Path.Combine(Directory, $"writer-audit-{time:yyyy-MM}.jsonl");

        public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter =>
            next => async (context, cancellationToken) =>
            {
                var name = context.Params?.Name;

                if (!IsTracked(name))
                {
                    return await next(context, cancellationToken);
                }

                // Copied now: a later filter removes 'portalId' from the arguments.
                var arguments = context.Params!.Arguments?.ToDictionary(a => a.Key, a => a.Value);
                var started = DateTime.Now;
                var watch = Stopwatch.StartNew();
                CallToolResult result;

                try
                {
                    result = await next(context, cancellationToken);
                }
                catch (Exception ex)
                {
                    Append(Record(name!, arguments, started, watch.ElapsedMilliseconds, false, ex.Message));
                    throw;
                }

                Append(Record(name!, arguments, started, watch.ElapsedMilliseconds, result.IsError != true, FirstText(result)));

                return result;
            };

        /// <summary>The JSON line for one call.</summary>
        public static string Record(string tool, IDictionary<string, JsonElement>? arguments, DateTime started, long durationMs, bool ok, string? message)
        {
            var args = new JsonObject();
            bool? dryRun = null;

            foreach (var argument in arguments ?? new Dictionary<string, JsonElement>())
            {
                args[argument.Key] = Compact(argument.Value);

                if (argument.Key == "dryRun")
                {
                    dryRun = argument.Value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.String when bool.TryParse(argument.Value.GetString(), out var parsed) => parsed,
                        _ => (bool?)null
                    };
                }
            }

            var record = new JsonObject
            {
                ["time"] = started.ToString("o"),
                ["tool"] = tool,
                ["dryRun"] = dryRun,
                ["ok"] = ok,
                ["durationMs"] = durationMs,
                ["args"] = args,
                ["message"] = Cut(message, MaxMessageChars)
            };

            return record.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        private static JsonNode? Compact(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return Cut(value.GetString(), MaxArgumentChars);

                case JsonValueKind.Number:
                    return value.TryGetInt64(out var integer) ? JsonValue.Create(integer) : JsonValue.Create(value.GetDouble());

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return null;

                default:
                    return Cut(value.GetRawText(), MaxArgumentChars);
            }
        }

        private static string? Cut(string? text, int max) =>
            text == null || text.Length <= max ? text : text.Substring(0, max) + $"...(+{text.Length - max} chars)";

        private static string? FirstText(CallToolResult result) =>
            result.Content?.OfType<TextContentBlock>().Select(c => c.Text).FirstOrDefault();

        private static void Append(string line)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(FilePath(DateTime.Now), line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                // The audit is evidence, not a gate: a full disk must not undo a write that worked.
                McpServer.Logger?.LogWarning(ex, "The audit record could not be written to {Directory}", Directory);
            }
        }
    }
}
