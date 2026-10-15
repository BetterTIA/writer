using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    /// <summary>One HMI tag to create: bound to a PLC path, or internal with a data type.</summary>
    public sealed class HmiTagSpec
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>PLC path for a bound tag ('"DB".Member' or 'Tag'); null for an internal tag.</summary>
        public string? PlcTag { get; set; }

        /// <summary>Data type of an internal tag; null for a bound tag (TIA Portal derives it).</summary>
        public string? DataType { get; set; }

        public bool Bound => PlcTag != null;
    }

    /// <summary>One alarm to create.</summary>
    public sealed class HmiAlarmSpec
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>The HMI tag, or 'Tag.Member', that raises the alarm.</summary>
        public string TriggerTag { get; set; } = string.Empty;

        /// <summary>Discrete alarms: the bit of the trigger tag.</summary>
        public uint BitNumber { get; set; }

        /// <summary>Analog alarms: one of <see cref="HmiSpecs.Conditions"/>.</summary>
        public string? Condition { get; set; }

        /// <summary>Analog alarms: an int or a double, as TIA Portal's setter takes them.</summary>
        public object? ConditionValue { get; set; }

        public string? EventText { get; set; }
    }

    /// <summary>
    /// Parsing and rules of the batch parameters of the HMI write tools (CreateHmiTags,
    /// CreateHmiAlarms), kept pure so Test13HmiSpecs covers them without TIA Portal.
    ///
    /// Items are separated by ';'. A tag is 'name=plcPath' (bound) or 'name:dataType' (internal).
    /// A discrete alarm is 'name|triggerTag|bitNumber|eventText', an analog alarm
    /// 'name|triggerTag|condition|value|eventText'; the texts are optional. Neither ';' nor '|'
    /// can therefore appear inside a text - a quoting rule if that is ever needed.
    /// </summary>
    public static class HmiSpecs
    {
        public static readonly string[] Conditions = { "LowerLimit", "UpperLimit", "Equal", "NotEqual", "LowerLimitOrEqual", "UpperLimitOrEqual" };

        public static readonly string[] TriggerModes = { "OnRisingEdge", "OnFallingEdge" };

        /// <summary>Why <paramref name="name"/> cannot name an HMI tag, table or alarm, or null when it can (TIA Portal's own rules, seen live upstream).</summary>
        public static string? NameProblem(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "the name is empty";
            }

            if (name!.Trim() != name)
            {
                return "the name starts or ends with a space";
            }

            if (name.IndexOfAny(new[] { '.', '"', ';', '|' }) >= 0 || name.Any(char.IsControl))
            {
                return "the name must not contain '.', '\"', ';', '|' or control characters";
            }

            return null;
        }

        private static string RequireName(string name)
        {
            var problem = NameProblem(name);

            if (problem != null)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"'{name}' is not a valid HMI name: {problem}.");
            }

            return name;
        }

        private static IEnumerable<string> Items(string specs) =>
            (specs ?? string.Empty).Split(';').Select(i => i.Trim()).Where(i => i.Length > 0);

        private static void RequireDistinct(IEnumerable<string> names)
        {
            var twice = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);

            if (twice != null)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"'{twice.Key}' appears twice in the batch.");
            }
        }

        public static List<HmiTagSpec> ParseTagSpecs(string specs)
        {
            var result = new List<HmiTagSpec>();

            foreach (var item in Items(specs))
            {
                var equals = item.IndexOf('=');
                var colon = item.IndexOf(':');

                if (equals > 0 && (colon < 0 || equals < colon))
                {
                    result.Add(new HmiTagSpec { Name = RequireName(item.Substring(0, equals).Trim()), PlcTag = RequireValue(item.Substring(equals + 1), item, "a PLC path") });
                }
                else if (colon > 0)
                {
                    result.Add(new HmiTagSpec { Name = RequireName(item.Substring(0, colon).Trim()), DataType = RequireValue(item.Substring(colon + 1), item, "a data type") });
                }
                else
                {
                    throw new PortalException(PortalErrorCode.InvalidParams,
                        $"'{item}' is not a tag. Write 'name=plcPath' for a tag bound to the PLC or 'name:dataType' for an internal tag, several separated by ';'.");
                }
            }

            if (result.Count == 0)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, "No tag was given.");
            }

            RequireDistinct(result.Select(t => t.Name));

            return result;
        }

        private static string RequireValue(string value, string item, string what)
        {
            var trimmed = value.Trim();

            if (trimmed.Length == 0)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"'{item}': {what} is missing after the separator.");
            }

            return trimmed;
        }

        /// <param name="discrete">True for 'name|triggerTag|bitNumber|eventText', false for 'name|triggerTag|condition|value|eventText'.</param>
        public static List<HmiAlarmSpec> ParseAlarmSpecs(string specs, bool discrete)
        {
            var result = new List<HmiAlarmSpec>();
            var shape = discrete ? "name|triggerTag|bitNumber|eventText" : "name|triggerTag|condition|value|eventText";

            foreach (var item in Items(specs))
            {
                var fields = item.Split('|').Select(f => f.Trim()).ToArray();
                var required = discrete ? 2 : 4;

                if (fields.Length < required || fields.Length > required + 2)
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, $"'{item}' is not an alarm. Write '{shape}', several separated by ';'.");
                }

                var spec = new HmiAlarmSpec { Name = RequireName(fields[0]), TriggerTag = RequireValue(fields[1], item, "the trigger tag") };

                if (discrete)
                {
                    if (fields.Length > 2 && fields[2].Length > 0)
                    {
                        if (!uint.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var bit))
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams, $"'{item}': '{fields[2]}' is not a bit number.");
                        }

                        spec.BitNumber = bit;
                    }

                    spec.EventText = fields.Length > 3 && fields[3].Length > 0 ? fields[3] : null;
                }
                else
                {
                    spec.Condition = Conditions.FirstOrDefault(c => c.Equals(fields[2], StringComparison.OrdinalIgnoreCase))
                        ?? throw new PortalException(PortalErrorCode.InvalidParams,
                            $"'{item}': '{fields[2]}' is not a condition. Use one of {string.Join(", ", Conditions)}.");
                    spec.ConditionValue = ParseNumber(fields[3], item);
                    spec.EventText = fields.Length > 4 && fields[4].Length > 0 ? fields[4] : null;
                }

                result.Add(spec);
            }

            if (result.Count == 0)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, "No alarm was given.");
            }

            RequireDistinct(result.Select(a => a.Name));

            return result;
        }

        /// <summary>An int when the text is one (the setter takes Int32 for integral limits), otherwise a double.</summary>
        public static object ParseNumber(string text, string item)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            {
                return integer;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && !double.IsNaN(number) && !double.IsInfinity(number))
            {
                return number;
            }

            throw new PortalException(PortalErrorCode.InvalidParams, $"'{item}': '{text}' is not a number (use '.' as the decimal separator).");
        }

        /// <summary>
        /// WinCC Unified stores alarm event texts as markup. Plain text is escaped and wrapped the
        /// way TIA Portal writes it; text that already starts with '&lt;body' is passed through.
        /// </summary>
        public static string EventTextMarkup(string text)
        {
            if (text.TrimStart().StartsWith("<body", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }

            return "<body><p>" + text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;") + "</p></body>";
        }
    }
}
