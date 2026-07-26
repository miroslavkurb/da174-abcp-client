using System.Text.Json;
using System.Text.Json.Serialization;

namespace ABCPClient.Application.Serialization;

/// <summary>
/// Параметры сериализации для ответов API ABCP.
/// </summary>
public static class AbcpJson
{
    /// <summary>
    /// Готовые параметры десериализации: имена полей без учёта регистра
    /// и конвертеры, устойчивые к смешанным типам значений.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>
    /// Создаёт новый набор параметров сериализации.
    /// </summary>
    public static JsonSerializerOptions Create()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Skip,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        options.Converters.Add(new AbcpDateTimeConverter());
        options.Converters.Add(new FlexibleDecimalConverter());
        options.Converters.Add(new FlexibleNullableDecimalConverter());
        options.Converters.Add(new FlexibleInt32Converter());
        options.Converters.Add(new FlexibleNullableInt32Converter());
        options.Converters.Add(new FlexibleInt64Converter());
        options.Converters.Add(new FlexibleBooleanConverter());

        return options;
    }
}
