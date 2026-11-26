using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaMcpServer.Siemens
{
    /// <summary>One screen item to create, as the JSON of CreateHmiScreenItems describes it.</summary>
    public sealed class HmiItemSpec
    {
        /// <summary>The Openness class without its 'Hmi' prefix (Button, Label, ScreenWindow...), or Faceplate.</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>The faceplate type a Faceplate item shows, e.g. 'Motor V 1.0.0'; only for Faceplate.</summary>
        [JsonPropertyName("faceplateType")]
        public string? FaceplateType { get; set; }

        [JsonPropertyName("left")]
        public int Left { get; set; }

        [JsonPropertyName("top")]
        public int Top { get; set; }

        /// <summary>For a circle the diameter: the circle is inscribed in the square at left/top.</summary>
        [JsonPropertyName("width")]
        public uint Width { get; set; }

        [JsonPropertyName("height")]
        public uint Height { get; set; }

        /// <summary>The text of a button or text field; refused on items without a Text property.</summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        /// <summary>Further properties by their Openness name or dotted path, e.g. BackColor, Font.Size.</summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, JsonElement>? Properties { get; set; }

        /// <summary>Dynamizations by property or dotted path: a tag name, or an object (see <see cref="HmiDynamizationSpec"/>).</summary>
        [JsonPropertyName("dynamizations")]
        public Dictionary<string, JsonElement>? Dynamizations { get; set; }

        /// <summary>Event scripts: event name to JavaScript, e.g. {"Tapped": "Tags('Start').Write(1);"}.</summary>
        [JsonPropertyName("events")]
        public Dictionary<string, string>? Events { get; set; }

        /// <summary>Property values as text, for the setter (filled by the parser).</summary>
        [JsonIgnore]
        public Dictionary<string, string> PropertyValues { get; set; } = new Dictionary<string, string>();

        /// <summary>The dynamizations, parsed (filled by the parser).</summary>
        [JsonIgnore]
        public Dictionary<string, HmiDynamizationSpec> DynamizationSpecs { get; set; } = new Dictionary<string, HmiDynamizationSpec>();
    }

    /// <summary>
    /// One dynamization of a property: a tag (TagDynamization, optionally indirect), a tag with a
    /// resource list (ResourceListDynamization) or a script (ScriptDynamization with a trigger).
    /// </summary>
    public sealed class HmiDynamizationSpec
    {
        [JsonPropertyName("tag")]
        public string? Tag { get; set; }

        /// <summary>The tag holds the name of the tag to show (UseIndirectAddressing).</summary>
        [JsonPropertyName("indirect")]
        public bool Indirect { get; set; }

        /// <summary>A text or graphic list that maps the value of the tag.</summary>
        [JsonPropertyName("resourceList")]
        public string? ResourceList { get; set; }

        /// <summary>JavaScript whose return value the property takes.</summary>
        [JsonPropertyName("script")]
        public string? Script { get; set; }

        /// <summary>When the script runs, by the Openness TriggerType name, e.g. T1s or AutomaticTags.</summary>
        [JsonPropertyName("trigger")]
        public string? Trigger { get; set; }

        /// <summary>
        /// Formula of the value converter of a tag dynamization (e.g. 'x * 0.1'); giving it turns the
        /// formula on unless formulaSelected says false.
        /// </summary>
        [JsonPropertyName("formula")]
        public string? Formula { get; set; }

        /// <summary>Switches the formula of the value converter on or off; null keeps it unless a formula is given.</summary>
        [JsonPropertyName("formulaSelected")]
        public bool? FormulaSelected { get; set; }

        /// <summary>The state IsFormulaSelected is set to, or null when the spec leaves the converter alone.</summary>
        [JsonIgnore]
        public bool? EffectiveFormulaSelected => FormulaSelected ?? (Formula != null ? true : (bool?)null);

        /// <summary>Short form for reports: 'tag X', 'script (T1s)'.</summary>
        public override string ToString() =>
            Script != null
                ? $"script ({Trigger ?? "default trigger"})"
                : $"tag {Tag}{(Indirect ? " (indirect)" : string.Empty)}{(ResourceList != null ? $" via {ResourceList}" : string.Empty)}{FormulaText(EffectiveFormulaSelected, Formula)}";

        /// <summary>' with formula ...' for a report, empty when the formula is off or untouched.</summary>
        public static string FormulaText(bool? selected, string? formula) =>
            selected == true ? $" with formula '{formula}'" : string.Empty;
    }

    /// <summary>
    /// Parsing and rules of the items of CreateHmiScreenItems, kept pure so Test14HmiScreenSpecs
    /// covers them without TIA Portal. The items come as a JSON array, because event scripts hold
    /// ';' and '|' and the batch format of HmiSpecs cannot carry them. The rules are checked
    /// against the Openness type of each item through reflection: a property, dynamization target
    /// or event the type does not have is refused before anything is created.
    /// </summary>
    public static class HmiScreenSpecs
    {
        /// <summary>The short name of the faceplate container, which Openness calls HmiFaceplateContainer.</summary>
        public const string Faceplate = "Faceplate";

        /// <summary>Properties the geometry and text fields set; they cannot be repeated in 'properties'.</summary>
        private static readonly string[] _reserved = { "Name", "Left", "Top", "Width", "Height", "CenterX", "CenterY", "Radius", "RadiusX", "RadiusY", "Text", "ContainedType" };

        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static List<HmiItemSpec> ParseItems(string json)
        {
            List<HmiItemSpec>? items;

            try
            {
                items = JsonSerializer.Deserialize<List<HmiItemSpec>>(json ?? string.Empty, _json);
            }
            catch (JsonException ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"items is not a JSON array of screen items: {ex.Message} Keys per item: type, name, faceplateType, left, top, width, height, text, properties, dynamizations, events.");
            }

            if (items == null || items.Count == 0)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, "No screen item was given; items must be a JSON array with at least one item.");
            }

            foreach (var item in items)
            {
                var problem = HmiSpecs.NameProblem(item.Name);

                if (problem != null)
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, $"'{item.Name}' is not a valid screen item name: {problem}.");
                }

                if (string.IsNullOrWhiteSpace(item.Type))
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, $"'{item.Name}' names no type, e.g. Button, Label, IOField, ScreenWindow.");
                }

                if (item.Type.Equals(Faceplate, StringComparison.OrdinalIgnoreCase) == string.IsNullOrWhiteSpace(item.FaceplateType))
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, $"'{item.Name}': faceplateType is needed by a Faceplate item and by no other.");
                }

                foreach (var property in item.Properties ?? new Dictionary<string, JsonElement>())
                {
                    item.PropertyValues[property.Key] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                }

                foreach (var dynamization in item.Dynamizations ?? new Dictionary<string, JsonElement>())
                {
                    item.DynamizationSpecs[dynamization.Key] = ParseDynamization(dynamization.Value, $"'{item.Name}', dynamization of '{dynamization.Key}'");
                }
            }

            var twice = items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);

            if (twice != null)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"'{twice.Key}' appears twice in the batch.");
            }

            return items;
        }

        /// <summary>A dynamization as given: a JSON string is a tag name, an object holds tag/indirect/resourceList or script/trigger.</summary>
        public static HmiDynamizationSpec ParseDynamization(JsonElement value, string what)
        {
            HmiDynamizationSpec? spec;

            try
            {
                spec = value.ValueKind == JsonValueKind.String
                    ? new HmiDynamizationSpec { Tag = value.GetString() }
                    : JsonSerializer.Deserialize<HmiDynamizationSpec>(value.GetRawText(), _json);
            }
            catch (JsonException ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"{what} is neither a tag name nor an object with tag, indirect, resourceList, script, trigger: {ex.Message}");
            }

            var tag = !string.IsNullOrWhiteSpace(spec?.Tag);
            var script = !string.IsNullOrWhiteSpace(spec?.Script);
            var problem = spec == null || tag == script ? "give either a tag or a script"
                : script && (spec.Indirect || spec.ResourceList != null) ? "indirect and resourceList belong to a tag, not a script"
                : tag && spec.Trigger != null ? "a trigger belongs to a script, not a tag"
                : (spec.Formula != null || spec.FormulaSelected != null) && (script || spec.ResourceList != null) ? "formula and formulaSelected belong to a plain tag dynamization (value converter), not to a script or a resource list"
                : spec.FormulaSelected == false && spec.Formula != null ? "formula is given while formulaSelected is false"
                : spec.EffectiveFormulaSelected == true && string.IsNullOrWhiteSpace(spec.Formula) ? "a formula turned on needs a non-empty formula"
                : null;

            if (problem != null)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"{what}: {problem}.");
            }

            return spec!;
        }

        /// <summary>A dynamization passed as a tool parameter: a JSON object, or else the plain name of a tag.</summary>
        public static HmiDynamizationSpec ParseDynamization(string text, string what)
        {
            var trimmed = (text ?? string.Empty).Trim();

            if (!trimmed.StartsWith("{"))
            {
                return ParseDynamization(JsonSerializer.SerializeToElement(trimmed), what);
            }

            try
            {
                return ParseDynamization(JsonDocument.Parse(trimmed).RootElement, what);
            }
            catch (JsonException ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"{what} is not valid JSON: {ex.Message}");
            }
        }

        /// <summary>Event scripts passed as a tool parameter: a JSON object of event name to JavaScript; empty is none.</summary>
        public static Dictionary<string, string>? ParseEvents(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json, _json);
            }
            catch (JsonException ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"events is not a JSON object of event name to script, e.g. {{\"Loaded\": \"...\"}}: {ex.Message}");
            }
        }

        /// <summary>
        /// The error of a batch that TIA Portal stopped inside its transaction: the item that
        /// failed, why, and that nothing of the batch stays - a transaction cannot be committed
        /// after a TIA Portal call threw, so it is rolled back as a whole.
        /// </summary>
        /// <param name="refused">Items the batch had refused before writing, as 'name: cause'.</param>
        public static string RolledBackBatch(string name, string type, string cause, IEnumerable<string> refused)
        {
            var others = refused.ToList();

            return $"'{name}' ({type}) failed: {cause}. TIA Portal does not commit a transaction after one of its calls failed, " +
                   "so the whole batch was rolled back and no item was created. Fix or drop that item, check the batch with dryRun and call again." +
                   (others.Count == 0 ? string.Empty : $" Refused before writing: {string.Join(" | ", others)}.");
        }

        /// <summary>
        /// Why <paramref name="spec"/> cannot be made from <paramref name="itemType"/> (the
        /// Openness class of the item), or null when it can. Checks the size, the text, the
        /// properties and the dynamization targets against the properties of the type, and the
        /// events against the enum its EventHandlers composition creates from.
        /// </summary>
        public static string? Problem(HmiItemSpec spec, Type itemType)
        {
            var round = Property(itemType, "Radius") != null;

            if (spec.Width == 0 || (spec.Height == 0 && !round))
            {
                return "width and height must be greater than 0 (a circle takes its diameter as width)";
            }

            if (spec.Text != null && Property(itemType, "Text") == null)
            {
                return $"a {spec.Type} has no text";
            }

            if (spec.Text != null && HmiSpecs.TextMarkupProblem(spec.Text) is string textProblem)
            {
                return $"Text: {textProblem}";
            }

            foreach (var name in spec.PropertyValues.Keys)
            {
                if (_reserved.Any(r => r.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    return $"'{name}' is set by the geometry or text fields, not under properties";
                }

                var property = PathProperty(itemType, name);

                if (property == null || !property.CanWrite)
                {
                    return $"a {spec.Type} has no writable property '{name}'";
                }
            }

            foreach (var dynamization in spec.DynamizationSpecs.Keys)
            {
                if (PathProperty(itemType, dynamization) == null)
                {
                    return $"a {spec.Type} has no property '{dynamization}' to dynamize";
                }
            }

            return EventsProblem(spec.Type, itemType, spec.Events);
        }

        /// <summary>Why the event scripts cannot be attached to <paramref name="type"/> (an item or a screen), or null.</summary>
        public static string? EventsProblem(string what, Type type, IDictionary<string, string>? events)
        {
            var eventType = EventType(type);

            foreach (var handler in events ?? new Dictionary<string, string>())
            {
                if (eventType == null)
                {
                    return $"a {what} has no events";
                }

                if (!Enum.GetNames(eventType).Any(n => n.Equals(handler.Key, StringComparison.OrdinalIgnoreCase) && n != "None"))
                {
                    return $"a {what} has no event '{handler.Key}'; it has {string.Join(", ", Enum.GetNames(eventType).Where(n => n != "None"))}";
                }

                if (string.IsNullOrWhiteSpace(handler.Value))
                {
                    return $"the event '{handler.Key}' has no script";
                }
            }

            return null;
        }

        /// <summary>
        /// The public property of that name, in any casing; the canonical name is in the result.
        /// A property a derived class redeclares (HmiToggleSwitch.EventHandlers) wins over the base one.
        /// </summary>
        public static PropertyInfo? Property(Type itemType, string name) =>
            itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => Depth(p.DeclaringType))
                .FirstOrDefault();

        private static int Depth(Type? type)
        {
            var depth = 0;

            for (; type != null; type = type.BaseType)
            {
                depth++;
            }

            return depth;
        }

        /// <summary>
        /// The property a dotted path ends at, or null: 'Font.Size' walks into the Font part;
        /// a segment that is a composition with Find(string) takes the next segment as the name
        /// of an element, so 'Interface.Speed.Value' is the Value of the interface entry 'Speed'.
        /// </summary>
        public static PropertyInfo? PathProperty(Type itemType, string path)
        {
            var segments = (path ?? string.Empty).Split('.');
            var type = itemType;

            for (var i = 0; ; i++)
            {
                var property = Property(type, segments[i]);

                if (property == null || i == segments.Length - 1)
                {
                    return property;
                }

                var find = Find(property.PropertyType);

                if (find != null && ++i == segments.Length - 1)
                {
                    return null;
                }

                type = find?.ReturnType ?? property.PropertyType;
            }
        }

        /// <summary>The Find(string) of a composition, or null for a plain object.</summary>
        public static MethodInfo? Find(Type type) => type.GetMethod("Find", new[] { typeof(string) });

        /// <summary>The enum the EventHandlers composition of the type creates from, or null when the type has none.</summary>
        public static Type? EventType(Type itemType)
        {
            var create = Property(itemType, "EventHandlers")?.PropertyType.GetMethod("Create");
            var parameter = create?.GetParameters().FirstOrDefault()?.ParameterType;

            return parameter != null && parameter.IsEnum ? parameter : null;
        }

        /// <summary>
        /// Geometry as the item's own properties hold it: centre and radius for a circle (the
        /// width is the diameter), centre and two radii for an ellipse, left/top/width/height else.
        /// </summary>
        public static Dictionary<string, string> Geometry(HmiItemSpec spec, Type itemType)
        {
            if (Property(itemType, "Radius") != null)
            {
                var radius = spec.Width / 2;

                return new Dictionary<string, string>
                {
                    ["CenterX"] = (spec.Left + (int)radius).ToString(),
                    ["CenterY"] = (spec.Top + (int)radius).ToString(),
                    ["Radius"] = radius.ToString()
                };
            }

            if (Property(itemType, "RadiusX") != null)
            {
                return new Dictionary<string, string>
                {
                    ["CenterX"] = (spec.Left + (int)(spec.Width / 2)).ToString(),
                    ["CenterY"] = (spec.Top + (int)(spec.Height / 2)).ToString(),
                    ["RadiusX"] = (spec.Width / 2).ToString(),
                    ["RadiusY"] = (spec.Height / 2).ToString()
                };
            }

            return new Dictionary<string, string>
            {
                ["Left"] = spec.Left.ToString(),
                ["Top"] = spec.Top.ToString(),
                ["Width"] = spec.Width.ToString(),
                ["Height"] = spec.Height.ToString()
            };
        }

        /// <summary>
        /// The value of a property as text, converted to the type of the property: an enum by
        /// name, a colour from '#RRGGBB', '#AARRGGBB' or a known colour name, numbers and booleans
        /// in invariant culture. An object-typed property (an HMI tag's InitialValue) keeps the text.
        /// </summary>
        public static object Convert(Type type, string text)
        {
            if (type.IsEnum)
            {
                return Enum.Parse(type, text.Trim(), true);
            }

            if (type == typeof(Color))
            {
                return ColorTranslator.FromHtml(text.Trim());
            }

            if (type == typeof(bool))
            {
                return bool.Parse(text.Trim());
            }

            if (type == typeof(object))
            {
                return text;
            }

            return System.Convert.ChangeType(text.Trim(), type, CultureInfo.InvariantCulture);
        }
    }
}
