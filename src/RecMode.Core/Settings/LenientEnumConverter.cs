using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecMode.Core.Settings;

/// <summary>
/// Reads an enum from JSON tolerantly (an unrecognized string/number value falls back to
/// <c>default(TEnum)</c> — or <c>null</c> for a nullable enum property — instead of throwing), writes it as
/// its string name like the stock <see cref="JsonStringEnumConverter"/> did. Without this, a single unknown
/// enum <em>value</em> anywhere in <c>settings.json</c> — an older build reading a newer build's file after a
/// new enum member was added, or a hand-edited typo — threw <see cref="JsonException"/> out of the
/// whole-document <c>Deserialize</c> call, which <see cref="SettingsService.Load"/> then treated as "the file
/// is corrupt": the entire settings document (every schedule, every custom profile, every hotkey remap) was
/// discarded, not just the one bad field. <see cref="RecModeSettings.UnknownProperties"/> already solved this
/// for unknown <em>properties</em>; this is the same problem one level down, for values.
/// </summary>
public sealed class LenientEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsEnum || Nullable.GetUnderlyingType(typeToConvert)?.IsEnum == true;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type? underlying = Nullable.GetUnderlyingType(typeToConvert);
        return underlying is not null
            ? (JsonConverter)Activator.CreateInstance(typeof(LenientNullableEnumConverter<>).MakeGenericType(underlying))!
            : (JsonConverter)Activator.CreateInstance(typeof(LenientEnumConverter<>).MakeGenericType(typeToConvert))!;
    }

    /// <summary>Parses one enum token, or returns <c>null</c> (and leaves the reader positioned so the caller
    /// must <see cref="Utf8JsonReader.Skip"/> it) if the token is missing/unrecognized/not a scalar.</summary>
    private static TEnum? TryRead<TEnum>(ref Utf8JsonReader reader) where TEnum : struct, Enum
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

        return null;
    }

    private sealed class LenientEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (TryRead<TEnum>(ref reader) is { } value)
            {
                return value;
            }

            // Unrecognized or non-scalar token (object/array/bool): skip it so the reader doesn't leave the
            // document mid-token — an un-skipped object/array token previously threw "read too much or not
            // enough" out of this converter, which Load() then treated as the whole file being corrupt, the
            // exact failure this converter exists to prevent — and fall back to the type's default member
            // rather than failing the whole document.
            reader.Skip();
            return default;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    /// <summary>Nullable counterpart: an unrecognized value yields <c>null</c>, not the enum's zero member,
    /// since for a nullable enum property <c>null</c> is usually a meaningful "unset" sentinel (e.g.
    /// <see cref="ScheduleItem.WeeklyDay"/>) that a zero-valued member would silently and wrongly stand in for.</summary>
    private sealed class LenientNullableEnumConverter<TEnum> : JsonConverter<TEnum?> where TEnum : struct, Enum
    {
        public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (TryRead<TEnum>(ref reader) is { } value)
            {
                return value;
            }

            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
        {
            if (value is { } v)
            {
                writer.WriteStringValue(v.ToString());
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
