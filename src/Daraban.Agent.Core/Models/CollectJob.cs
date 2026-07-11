using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daraban.Agent.Core.Models;

[JsonConverter(typeof(CollectJobTypeConverter))]
public enum CollectJobType
{
    RegistryKey = 1,
    WmiQuery = 2,
    FileContent = 3,
    Command = 4
}

/// <summary>
/// A single collect job definition pushed down from the server.
/// Maps to GLPI-agent's Collect task job schema.
/// </summary>
public sealed class CollectJob
{
    public string JobId { get; init; } = string.Empty;
    public CollectJobType Type { get; init; }

    // RegistryKey
    public string? RegistryHive { get; init; }   // e.g. "HKLM"
    public string? RegistryPath { get; init; }
    public string? RegistryValue { get; init; }   // null = read default

    // WmiQuery
    public string? WmiNamespace { get; init; }   // e.g. "root\\cimv2"
    public string? WmiQuery { get; init; }
    public string? WmiProperty { get; init; }

    // FileContent
    public string? FilePath { get; init; }
    public string? FileRegex { get; init; }   // optional line-filter

    // Command
    public string? Command { get; init; }
    public string? Arguments { get; init; }
}

/// <summary>
/// Handles all three JSON formats:
///   "type": "1"           (string integer  — your current JSON)
///   "type": 1             (bare integer)
///   "type": "RegistryKey" (enum name)
/// </summary>
public sealed class CollectJobTypeConverter : JsonConverter<CollectJobType>
{
    public override CollectJobType Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // "type": 1  — bare integer
        if (reader.TokenType == JsonTokenType.Number)
        {
            return (CollectJobType)reader.GetInt32();
        }

        // "type": "1" or "type": "RegistryKey"
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString() ?? string.Empty;

            // Try parse as integer string first → "1", "2", "3", "4"
            if (int.TryParse(str, out var intVal))
                return (CollectJobType)intVal;

            // Try parse as enum name → "RegistryKey", "WmiQuery", etc.
            if (Enum.TryParse<CollectJobType>(str, ignoreCase: true, out var enumVal))
                return enumVal;
        }

        throw new JsonException(
            $"Cannot convert '{reader.GetString()}' to {nameof(CollectJobType)}. " +
            $"Use 1-4 or: RegistryKey, WmiQuery, FileContent, Command");
    }

    public override void Write(
        Utf8JsonWriter writer,
        CollectJobType value,
        JsonSerializerOptions options)
    {
        // Always write as the enum name when serializing output
        writer.WriteStringValue(value.ToString());
    }
}