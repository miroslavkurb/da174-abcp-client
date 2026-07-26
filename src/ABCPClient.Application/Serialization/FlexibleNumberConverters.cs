using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ABCPClient.Application.Serialization;

/// <summary>
/// Общий разбор числовых значений API ABCP.
/// </summary>
/// <remarks>
/// В ответах одно и то же поле встречается и числом (<c>"price": 231</c>),
/// и строкой (<c>"weight": "1.76"</c>, <c>"availability": "1943"</c>), и пустой строкой.
/// Разделителем дробной части всегда выступает точка, поэтому разбор идёт
/// в инвариантной культуре, а не в культуре машины.
/// </remarks>
internal static class FlexibleNumberReader
{
    /// <summary>Читает значение как строку независимо от типа токена.</summary>
    internal static string? ReadRaw(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return reader.GetString();

            case JsonTokenType.Number:
                return reader.GetDouble().ToString("R", CultureInfo.InvariantCulture);

            case JsonTokenType.True:
                return "1";

            case JsonTokenType.False:
                return "0";

            default:
                reader.Skip();
                return null;
        }
    }

    /// <summary>Разбирает десятичное число.</summary>
    internal static decimal? ParseDecimal(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? null
            : decimal.TryParse(
                raw.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimal value)
                ? value
                : null;

    /// <summary>Разбирает целое число, допуская дробную запись вида <c>"3.0"</c>.</summary>
    internal static long? ParseInteger(string? raw)
    {
        decimal? value = ParseDecimal(raw);
        return value is null ? null : (long)Math.Truncate(value.Value);
    }
}

/// <summary>Конвертер <see cref="decimal"/>, устойчивый к строковым значениям.</summary>
public sealed class FlexibleDecimalConverter : JsonConverter<decimal>
{
    /// <inheritdoc />
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        FlexibleNumberReader.ParseDecimal(FlexibleNumberReader.ReadRaw(ref reader)) ?? 0m;

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value);
    }
}

/// <summary>Конвертер <see cref="Nullable{Decimal}"/>, устойчивый к строковым значениям.</summary>
public sealed class FlexibleNullableDecimalConverter : JsonConverter<decimal?>
{
    /// <inheritdoc />
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        FlexibleNumberReader.ParseDecimal(FlexibleNumberReader.ReadRaw(ref reader));

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(value.Value);
        }
    }
}

/// <summary>Конвертер <see cref="int"/>, устойчивый к строковым значениям.</summary>
public sealed class FlexibleInt32Converter : JsonConverter<int>
{
    /// <inheritdoc />
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        (int?)FlexibleNumberReader.ParseInteger(FlexibleNumberReader.ReadRaw(ref reader)) ?? 0;

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value);
    }
}

/// <summary>Конвертер <see cref="Nullable{Int32}"/>, устойчивый к строковым значениям.</summary>
public sealed class FlexibleNullableInt32Converter : JsonConverter<int?>
{
    /// <inheritdoc />
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        (int?)FlexibleNumberReader.ParseInteger(FlexibleNumberReader.ReadRaw(ref reader));

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(value.Value);
        }
    }
}

/// <summary>Конвертер <see cref="long"/>, устойчивый к строковым значениям.</summary>
public sealed class FlexibleInt64Converter : JsonConverter<long>
{
    /// <inheritdoc />
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        FlexibleNumberReader.ParseInteger(FlexibleNumberReader.ReadRaw(ref reader)) ?? 0L;

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value);
    }
}

/// <summary>
/// Конвертер <see cref="bool"/> для флагов API, которые приходят как <c>1</c>, <c>"1"</c>,
/// <c>true</c> или <c>"true"</c>.
/// </summary>
public sealed class FlexibleBooleanConverter : JsonConverter<bool>
{
    /// <inheritdoc />
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.True)
        {
            return true;
        }

        if (reader.TokenType is JsonTokenType.False or JsonTokenType.Null)
        {
            return false;
        }

        string? raw = FlexibleNumberReader.ReadRaw(ref reader);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string value = raw.Trim();
        if (bool.TryParse(value, out bool parsed))
        {
            return parsed;
        }

        return FlexibleNumberReader.ParseDecimal(value) is { } number && number != 0m;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBooleanValue(value);
    }
}
