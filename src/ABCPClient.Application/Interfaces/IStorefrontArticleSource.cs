using ABCPClient.Application.DTO;

namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Сведения о детали со страницы товара на витрине магазина.
/// </summary>
/// <remarks>
/// Витрина — обычный сайт магазина, а не API: её страницы лимит вызовов API
/// не расходуют. Это единственный бесплатный источник для деталей под заказ,
/// которых нет в выгрузке каталога (в ней только собственное наличие).
/// </remarks>
public interface IStorefrontArticleSource
{
    /// <summary>Настроен ли адрес витрины.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает страницу товара и возвращает найденные сведения.
    /// </summary>
    /// <param name="article">Бренд и номер детали.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Сведения о детали либо <c>null</c>, если витрина не настроена
    /// или страница недоступна. Страница без товара возвращается как
    /// <see cref="StorefrontArticle"/> с пустыми полями: это тоже ответ,
    /// и повторно её запрашивать не нужно.
    /// </returns>
    Task<StorefrontArticle?> GetAsync(ArticleRef article, CancellationToken cancellationToken = default);
}
