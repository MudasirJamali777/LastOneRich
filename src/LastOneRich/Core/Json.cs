using System.Text.Json;
using System.Text.Json.Serialization;

namespace LastOneRich.Core;

/// <summary>Tiny JSON loader for all data-driven content (GDD section 13).</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Write options share Options' semantics (case-insensitive, field-inclusive) and only add
    /// indentation. Previously Save() used a fresh options object, so anything written here did not
    /// round-trip through Load() identically — matters now that saves/settings.json is a live file.
    /// WriteIndented only affects layout, never parsing, so reusing it for reads stays safe.
    /// </summary>
    public static readonly JsonSerializerOptions WriteOptions = new(Options) { WriteIndented = true };

    public static string ContentRoot => Path.Combine(AppContext.BaseDirectory, "content");

    public static string PathFor(string relative) => Path.Combine(ContentRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    public static T Load<T>(string relative)
    {
        string text = File.ReadAllText(PathFor(relative));
        return JsonSerializer.Deserialize<T>(text, Options)
               ?? throw new InvalidDataException($"Failed to deserialize {relative}");
    }

    public static void Save<T>(string fullPath, T value)
    {
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(value, WriteOptions));
    }
}
