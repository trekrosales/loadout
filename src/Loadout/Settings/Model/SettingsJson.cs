// CONTRACT (owned by the lead). The one and only way to read and write settings.json.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loadout.Settings.Model;

/// <summary>
/// JSON options that produce exactly the settings.json format shown in SPEC section 10:
/// camelCase names, enums as camelCase strings ("app", "folder", "website"), null for empty slots,
/// indented output. Reading is forgiving: names are case-insensitive, and comments and trailing commas are allowed,
/// since people may edit the file by hand.
/// </summary>
public static class SettingsJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(LoadoutSettings settings) =>
        JsonSerializer.Serialize(settings, Options);

    /// <summary>Throws <see cref="JsonException"/> on invalid JSON. Returns null for the JSON literal null.</summary>
    public static LoadoutSettings? Deserialize(string json) =>
        JsonSerializer.Deserialize<LoadoutSettings>(json, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
