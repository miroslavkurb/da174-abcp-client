using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ABCPClient.Application.Serialization;

/// <summary>
/// Читает даты API ABCP в формате <c>ГГГГ-ММ-ДД ЧЧ:ММ:СС</c>.
/// </summary>
/// <remarks>
/// Время приходит во времени портала, поэтому значение сохраняется как
/// <see cref="DateTimeKind.Unspecified"/> — без перевода в UTC и локальное время.
/// Пустые строки и нулевые даты вида <c>0000-00-00 00:00:00</c> считаются отсутствующими.
/// </remarks>
public sealed class AbcpDateTimeConverter : JsonConverter<DateTime?>
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:sszzz",
    ];

    /// <inheritdoc />
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                string? raw = reader.GetString();
                return Parse(raw);

            default:
                reader.Skip();
                return null;
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString(Formats[0], CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Разбирает строку даты API. Возвращает <c>null</c>, если значение отсутствует или некорректно.
    /// </summary>
    /// <param name="raw">Строка из ответа API.</param>
    public static DateTime? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string value = raw.Trim();

        // MySQL-подобные «нулевые» даты означают отсутствие значения.
        if (value.StartsWith("0000-00-00", StringComparison.Ordinal))
        {
            return null;
        }

        if (DateTime.TryParseExact(
                value,
                Formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.NoCurrentDateDefault,
                out DateTime parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified)
            : null;
    }
}
