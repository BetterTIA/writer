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
        /// <summary>Button, IOField, Text, Rectangle or Circle (see <see cref="HmiScreenSpecs.ItemTypes"/>).</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

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

        /// <summary>Further properties of the item by their Openness name, e.g. BackColor, Visible, ProcessValue.</summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, JsonElement>? Properties { get; set; }

        /// <summary>Tag dynamizations: property name to HMI tag, e.g. {"ProcessValue": "Speed"}.</summary>
        [JsonPropertyName("dynamizations")]
        public Dictionary<string, string>? Dynamizations { get; set; }

        /// <summary>Event scripts: event name to JavaScript, e.g. {"Tapped": "Tags('Start').Write(1);"}.</summary>
        [JsonPropertyName("events")]
        public Dictionary<string, string>? Events { get; set; }

        /// <summary>Property values as text, for the setter (filled by the parser).</summary>
        [JsonIgnore]
        public Dictionary<string, string> PropertyValues { get; set; } = new Dictionary<string, string>();
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
        /// <summary>The item types of this version, by the short name the JSON uses.</summary>
        public static readonly string[] ItemTypes = { "Button", "IOField", "Text", "Rectangle", "Circle" };

        /// <summary>Properties the geometry and text fields set; they cannot be repeated in 'properties'.</summary>
        private static readonly string[] _reserved = { "Name", "Left", "Top", "Width", "Height", "CenterX", "CenterY", "Radius", "Text" };

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
                    $"items is not a JSON array of screen items: {ex.Message} Keys per item: type, name, left, top, width, height, text, properties, dynamizations, events.");
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

                var type = ItemTypes.FirstOrDefault(t => t.Equals(item.Type, StringComparison.OrdinalIgnoreCase))
                    ?? throw new PortalException(PortalErrorCode.InvalidParams,
                        $"'{item.Name}': '{item.Type}' is not an item type. Use one of {string.Join(", ", ItemTypes)}.");

                item.Type = type;

                if (item.Width == 0 || (item.Height == 0 && type != "Circle"))
                {
                    throw new PortalException(PortalErrorCode.InvalidParams,
                        $"'{item.Name}': width and height must be greater than 0 (a circle takes its diameter as width).");
                }

                foreach (var property in item.Properties ?? new Dictionary<string, JsonElement>())
                {
                    item.PropertyValues[property.Key] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                }
            }

            var twice = items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);

            if (twice != null)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"'{twice.Key}' appears twice in the batch.");
            }

            return items;
        }

        /// <summary>
        /// Why <paramref name="spec"/> cannot be made from <paramref name="itemType"/> (the
        /// Openness class of the item), or null when it can. Checks the text, the properties and
        /// the dynamization targets against the writable properties of the type, and the events
        /// against the enum its EventHandlers composition creates from.
        /// </summary>
        public static string? Problem(HmiItemSpec spec, Type itemType)
        {
            if (spec.Text != null && Property(itemType, "Text") == null)
            {
                return $"a {spec.Type} has no text";
            }

            foreach (var name in spec.PropertyValues.Keys)
            {
                if (_reserved.Any(r => r.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    return $"'{name}' is set by the geometry or text fields, not under properties";
                }

                var property = Property(itemType, name);

                if (property == null || !property.CanWrite)
                {
                    return $"a {spec.Type} has no writable property '{name}'";
                }
            }

            foreach (var dynamization in spec.Dynamizations ?? new Dictionary<string, string>())
            {
                if (Property(itemType, dynamization.Key) == null)
                {
                    return $"a {spec.Type} has no property '{dynamization.Key}' to dynamize";
                }

                if (string.IsNullOrWhiteSpace(dynamization.Value))
                {
                    return $"the dynamization of '{dynamization.Key}' names no tag";
                }
            }

            var events = EventType(itemType);

            foreach (var handler in spec.Events ?? new Dictionary<string, string>())
            {
                if (events == null)
                {
                    return $"a {spec.Type} has no events";
                }

                if (!Enum.GetNames(events).Any(n => n.Equals(handler.Key, StringComparison.OrdinalIgnoreCase) && n != "None"))
                {
                    return $"a {spec.Type} has no event '{handler.Key}'; it has {string.Join(", ", Enum.GetNames(events).Where(n => n != "None"))}";
                }

                if (string.IsNullOrWhiteSpace(handler.Value))
                {
                    return $"the event '{handler.Key}' has no script";
                }
            }

            return null;
        }

        /// <summary>The public property of that name, in any casing; the canonical name is in the result.</summary>
        public static PropertyInfo? Property(Type itemType, string name) =>
            itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The enum the EventHandlers composition of the type creates from, or null when the type has none.</summary>
        public static Type? EventType(Type itemType)
        {
            var create = Property(itemType, "EventHandlers")?.PropertyType.GetMethod("Create");
            var parameter = create?.GetParameters().FirstOrDefault()?.ParameterType;

            return parameter != null && parameter.IsEnum ? parameter : null;
        }

        /// <summary>
        /// The value of a property as text, converted to the type of the property: an enum by
        /// name, a colour from '#RRGGBB', '#AARRGGBB' or a known colour name, numbers and booleans
        /// in invariant culture.
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

            return System.Convert.ChangeType(text.Trim(), type, CultureInfo.InvariantCulture);
        }
    }
}
