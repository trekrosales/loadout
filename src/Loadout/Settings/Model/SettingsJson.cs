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
        options.Converters.Add(new TolerantSlotTypeConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>
    /// Reads an unknown slot type (a typo, or a type from a newer Loadout) as an undefined value instead of failing,
    /// so only that slot is emptied by the normalizer, not the whole file.
    /// </summary>
    private sealed class TolerantSlotTypeConverter : JsonConverter<SlotType>
    {
        public const SlotType Unknown = (SlotType)(-1);

        public override SlotType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.TryGetInt32(out var number) ? (SlotType)number : Unknown;
            }

            if (reader.TokenType == JsonTokenType.String
                && Enum.TryParse<SlotType>(reader.GetString(), ignoreCase: true, out var value)
                && Enum.IsDefined(value)
                && !int.TryParse(reader.GetString(), out _))
            {
                return value;
            }

            return Unknown;
        }

        public override void Write(Utf8JsonWriter writer, SlotType value, JsonSerializerOptions options)
        {
            var name = Enum.IsDefined(value) ? value.ToString() : null;
            if (name is null)
            {
                writer.WriteNumberValue((int)value);
                return;
            }

            writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(name));
        }
    }
}
