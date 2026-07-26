namespace ABCPClient.Application.DTO;

/// <summary>
/// Сведения о детали, прочитанные со страницы товара на витрине магазина.
/// </summary>
/// <param name="ImageUrl">Полный адрес изображения или <c>null</c>.</param>
/// <param name="Description">Наименование детали без бренда и артикула или <c>null</c>.</param>
public sealed record StorefrontArticle(string? ImageUrl, string? Description)
{
    /// <summary>На странице ничего не нашлось: такого товара витрина не знает.</summary>
    public bool IsEmpty => ImageUrl is null && Description is null;
}
