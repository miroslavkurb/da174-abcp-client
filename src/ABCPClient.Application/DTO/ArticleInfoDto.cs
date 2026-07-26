using System.Text.Json.Serialization;
using ABCPClient.Domain.Models;

namespace ABCPClient.Application.DTO;

/// <summary>
/// Ссылка на деталь: бренд и номер. Пара «бренд + номер» — то, чем API опознаёт товар.
/// </summary>
/// <param name="Brand">Имя производителя.</param>
/// <param name="Number">Номер детали.</param>
public sealed record ArticleRef(string Brand, string Number)
{
    /// <summary>Ключ для сопоставления без учёта регистра.</summary>
    public string Key => ArticleKey.Exact(Brand, Number);

    /// <summary>
    /// Ключ для сопоставления между источниками: без разделителей и регистра.
    /// </summary>
    public string MatchKey => ArticleKey.Match(Brand, Number);
}

/// <summary>
/// Изображение детали в карточке товара.
/// </summary>
public sealed class ArticleImageDto
{
    /// <summary>
    /// Имя файла изображения. Полный адрес — <c>https://imgcdn.abcp.ru/p/{name}</c>.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Порядок вывода.</summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }
}

/// <summary>
/// Карточка товара из операции <c>cp/articles/info/batch</c>.
/// </summary>
public sealed class ArticleInfoDto
{
    /// <summary>Имя производителя.</summary>
    [JsonPropertyName("brand")]
    public string Brand { get; set; } = string.Empty;

    /// <summary>
    /// Номер детали в «очищенном» виде: API возвращает его без разделителей
    /// (<c>ADW0855</c> вместо <c>ADW-0855</c>).
    /// </summary>
    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    /// <summary>
    /// Номер детали в исходном виде, как он записан у производителя.
    /// Нужен для сопоставления с позициями заказа.
    /// </summary>
    [JsonPropertyName("outer_number")]
    public string? OuterNumber { get; set; }

    /// <summary>Локализованное описание детали.</summary>
    [JsonPropertyName("descr")]
    public string? Description { get; set; }

    /// <summary>Изображения детали.</summary>
    [JsonPropertyName("images")]
    public List<ArticleImageDto> Images { get; set; } = [];

    /// <summary>Количество изображений.</summary>
    [JsonPropertyName("images_count")]
    public int ImagesCount { get; set; }

    /// <summary>
    /// Локализованные свойства детали. Значение бывает как строкой, так и массивом строк
    /// (например, «Допуски производителей»), поэтому хранится в виде текстового элемента JSON.
    /// </summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, System.Text.Json.JsonElement> Properties { get; set; } = [];

    /// <summary>Ссылка на деталь по «очищенному» номеру.</summary>
    public ArticleRef ToRef() => new(Brand, Number);

    /// <summary>
    /// Ключи, по которым карточку можно сопоставить с позицией заказа:
    /// исходный и «очищенный» номер.
    /// </summary>
    public IEnumerable<string> GetMatchKeys()
    {
        yield return ToRef().Key;

        if (!string.IsNullOrWhiteSpace(OuterNumber))
        {
            yield return new ArticleRef(Brand, OuterNumber).Key;
        }
    }

    /// <summary>
    /// Возвращает свойства в виде плоских строк: массивы склеиваются через запятую.
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>> GetFlatProperties()
    {
        foreach ((string name, System.Text.Json.JsonElement value) in Properties)
        {
            string? text = value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => value.GetString(),
                System.Text.Json.JsonValueKind.Number => value.ToString(),
                System.Text.Json.JsonValueKind.True => "да",
                System.Text.Json.JsonValueKind.False => "нет",
                System.Text.Json.JsonValueKind.Array => string.Join(
                    ", ",
                    value.EnumerateArray().Select(item => item.ToString())),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return new KeyValuePair<string, string>(name, text);
            }
        }
    }
}
