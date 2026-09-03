using Daraban.Agent.Core.Models;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace Daraban.Agent.Core.Collectors;

/// <summary>
/// Merges a user-supplied content file into the collected inventory before sending,
/// mirroring glpi-agent's `additional-content` option.
///
/// Supported formats (decided by extension):
///  - .json: the file's `content` object is merged property-by-property into DeviceContent.
///    Matching property names overwrite the collected value (useful for custom fields the
///    server expects, or for overriding values without editing collectors).
///  - .xml:   the file's &lt;content&gt; node children are mapped to DeviceContent properties
///    by element name.
///
/// Unknown fields are ignored (forward-compatible with newer server schemas).
/// </summary>
public static class ContentMerger
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Merges the additional-content file (if any) into <paramref name="base"/>.
    /// Returns the merged content; when the file is missing or unparseable, returns the
    /// base unchanged.
    /// </summary>
    public static DeviceContent Merge(DeviceContent baseContent, string? contentFilePath)
    {
        if (string.IsNullOrWhiteSpace(contentFilePath) || !File.Exists(contentFilePath))
            return baseContent;

        try
        {
            var ext = Path.GetExtension(contentFilePath).ToLowerInvariant() switch
            {
                ".json" or ".jsonc" => "json",
                ".xml" => "xml",
                _ => "json"
            };

            return ext == "xml"
                ? MergeXml(baseContent, contentFilePath)
                : MergeJson(baseContent, contentFilePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[content] additional-content merge failed for '{contentFilePath}': {ex.Message}");
            return baseContent;
        }
    }

    // ── JSON ───────────────────────────────────────────────────────────────────

    private static DeviceContent MergeJson(DeviceContent baseContent, string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);

        // Accept either {"content": {...}} (GLPI shape) or a bare DeviceContent object.
        var root = doc.RootElement;
        if (root.TryGetProperty("content", out var contentEl))
            root = contentEl;

        var props = typeof(DeviceContent).GetProperties();

        foreach (var prop in props)
        {
            if (!TryGetJsonProperty(root, prop.Name, out var value))
                continue;

            var jsonValue = value.GetRawText();
            try
            {
                var typed = JsonSerializer.Deserialize(jsonValue, prop.PropertyType, JsonOpts);
                if (typed is not null)
                    prop.SetValue(baseContent, typed);
            }
            catch (JsonException)
            {
                // Field present but incompatible type — leave the collected value.
            }
        }

        return baseContent;
    }

    // ── XML ────────────────────────────────────────────────────────────────────

    private static DeviceContent MergeXml(DeviceContent baseContent, string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root;

        // Find the <content> wrapper if present, else use the document root.
        var contentNode = root?.Element("content") ?? root;
        if (contentNode is null)
            return baseContent;

        var props = typeof(DeviceContent).GetProperties();

        foreach (var element in contentNode.Elements())
        {
            var prop = FindProperty(props, element.Name.LocalName);
            if (prop is null)
                continue;

            // Empty <field></field> means "clear"; non-empty parses per property type.
            if (string.IsNullOrWhiteSpace(element.Value))
            {
                prop.SetValue(baseContent, null);
                continue;
            }

            try
            {
                if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(baseContent, element.Value);
                }
                else
                {
                    var typed = JsonSerializer.Deserialize($"\"{EscapeJson(element.Value)}\"", prop.PropertyType, JsonOpts);
                    if (typed is not null)
                        prop.SetValue(baseContent, typed);
                }
            }
            catch
            {
                // Unparseable element — ignore.
            }
        }

        return baseContent;
    }

    /// <summary>Matches a JSON property name (camelCase) or C# property name to a property.</summary>
    private static bool TryGetJsonProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        // JSON from our own envelope is camelCase; try it first.
        var camel = JsonNamingPolicy.CamelCase.ConvertName(propertyName);
        if (root.TryGetProperty(camel, out value) || root.TryGetProperty(propertyName, out value))
            return true;
        return false;
    }

    private static PropertyInfo? FindProperty(IEnumerable<PropertyInfo> props, string name)
    {
        foreach (var p in props)
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(JsonNamingPolicy.CamelCase.ConvertName(p.Name), name, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }

    private static string EscapeJson(string value)
        => JsonSerializer.Serialize(value);
}