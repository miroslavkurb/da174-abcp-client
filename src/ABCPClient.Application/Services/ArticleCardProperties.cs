using System.Text.Json;

namespace ABCPClient.Application.Services;

/// <summary>
/// Разбор свойств карточки товара, сохранённых в виде JSON.
/// </summary>
public static class ArticleCardProperties
{
    /// <summary>
    /// Возвращает свойства плоским списком «название — значение».
    /// </summary>
    /// <remarks>
    /// Значение свойства бывает и строкой, и числом, и массивом строк
    /// (например, «Допуски производителей»), поэтому массивы склеиваются через запятую.
    /// </remarks>
    /// <param name="propertiesJson">Свойства в виде JSON-объекта.</param>
    public static IEnumerable<KeyValuePair<string, string>> Flatten(string? propertiesJson)
    {
        if (string.IsNullOrWhiteSpace(propertiesJson))
        {
            yield break;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(propertiesJson);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                yield break;
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                string? text = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.ToString(),
                    JsonValueKind.True => "да",
                    JsonValueKind.False => "нет",
                    JsonValueKind.Array => string.Join(
                        ", ",
                        property.Value.EnumerateArray().Select(item => item.ToString())),
                    _ => null,
                };

                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return new KeyValuePair<string, string>(property.Name, text);
                }
            }
        }
    }
}
