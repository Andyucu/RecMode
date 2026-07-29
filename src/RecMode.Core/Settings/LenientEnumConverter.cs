using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecMode.Core.Settings;

/// <summary>
/// Reads an enum from JSON tolerantly (an unrecognized string/number value falls back to
/// <c>default(TEnum)</c> instead of throwing), writes it as its string name like the stock
/// <see cref="JsonStringEnumConverter"/> did. Without this, a single unknown enum <em>value</em> anywhere in
/// <c>settings.json</c> — an older build reading a newer build's file after a new enum member was added, or a
/// hand-edited typo — threw <see cref="JsonException"/> out of the whole-document <c>Deserialize</c> call,
/// which <see cref="SettingsService.Load"/> then treated as "the file is corrupt": the entire settings
/// document (every schedule, every custom profile, every hotkey remap) was discarded, not just the one bad
/// field. <see cref="RecModeSettings.UnknownProperties"/> already solved this for unknown <em>properties</em>;
/// this is the same problem one level down, for values.
/// </summary>
public sealed class LenientEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(LenientEnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class LenientEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? s = reader.GetString();
                if (s is not null && Enum.TryParse(s, ignoreCase: true, out TEnum value) && Enum.IsDefined(value))
                {
                    return value;
                }
            }
            else if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long n))
            {
                TEnum value = (TEnum)Enum.ToObject(typeof(TEnum), n);
                if (Enum.IsDefined(value))
                {
                    return value;
                }
            }

            // Unrecognized value: fall back to the type's default member rather than failing the whole
            // document. This one field silently resets to a default (same outcome as if it were simply
            // missing from the JSON) instead of every other setting in the file being thrown away with it.
            return default;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
