using ABCPClient.Application.DTO;
using ABCPClient.Domain.Entities;

namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Доступ к кэшу карточек товаров в локальной базе.
/// </summary>
public interface IArticleCardRepository
{
    /// <summary>
    /// Возвращает сохранённые карточки для указанных деталей.
    /// </summary>
    /// <param name="articles">Детали.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Карточки по ключу <see cref="ArticleRef.Key"/>.</returns>
    Task<IReadOnlyDictionary<string, ArticleCard>> GetAsync(
        IReadOnlyCollection<ArticleRef> articles,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет или обновляет карточки.
    /// </summary>
    /// <param name="cards">Карточки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task UpsertAsync(IReadOnlyCollection<ArticleCard> cards, CancellationToken cancellationToken = default);

    /// <summary>
    /// Находит карточку по штрихкоду.
    /// </summary>
    /// <remarks>
    /// Основной способ опознать товар на терминале сборки: сканер отдаёт штрихкод,
    /// а не бренд с артикулом. Штрихкоды попадают в кэш из выгрузки каталога —
    /// API их не отдаёт вовсе, поэтому покрытие неполное и поиск по артикулу
    /// остаётся обязательным, а не запасным.
    /// </remarks>
    /// <param name="barcode">Штрихкод целиком.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<ArticleCard?> FindByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ищет карточки по части артикула, бренда или наименования.
    /// </summary>
    /// <param name="query">Строка поиска.</param>
    /// <param name="limit">Сколько карточек вернуть не более.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyList<ArticleCard>> SearchAsync(
        string query,
        int limit = 50,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Карточки товаров: локальный кэш плюс дозированные обращения к API.
/// </summary>
public interface IArticleCardService
{
    /// <summary>
    /// Возвращает карточки для деталей: сначала из локального кэша,
    /// отсутствующие — из API с соблюдением ограничения на частоту запросов.
    /// </summary>
    /// <param name="articles">Детали.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<ArticleCardsResult> GetCardsAsync(
        IReadOnlyCollection<ArticleRef> articles,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Результат получения карточек товаров.
/// </summary>
/// <param name="Cards">Карточки по ключу <see cref="ArticleRef.Key"/>.</param>
/// <param name="FromCache">Сколько карточек взято из локального кэша.</param>
/// <param name="FetchedFromApi">Сколько карточек получено из API.</param>
/// <param name="FetchedFromStorefront">
/// Сколько карточек получено со страниц витрины магазина: лимит вызовов API
/// эти обращения не расходуют.
/// </param>
/// <param name="NotRequested">
/// Сколько деталей осталось без карточки: не хватило лимита запросов
/// или API временно заблокировал обращения.
/// </param>
/// <param name="RateLimited">API ответил ошибкой 303 — лимит запросов исчерпан.</param>
public sealed record ArticleCardsResult(
    IReadOnlyDictionary<string, ArticleCard> Cards,
    int FromCache,
    int FetchedFromApi,
    int FetchedFromStorefront,
    int NotRequested,
    bool RateLimited);
