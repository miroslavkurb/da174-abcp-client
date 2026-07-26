using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;
using ABCPClient.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace ABCPClient.Infrastructure.Repositories;

/// <summary>
/// Кэш карточек товаров в локальной базе.
/// </summary>
/// <remarks>
/// Поиск идёт по сопоставительному ключу <see cref="ArticleCard.MatchKey"/>, а не по
/// паре «бренд + номер» как есть: в заказе артикул записан <c>ADW-0855</c>, а в выгрузке
/// каталога <c>ADW0855</c>, и при точном сравнении карточка из каталога не находилась бы.
/// Сравнение строк в SQLite к тому же чувствительно к регистру, поэтому ключ заранее
/// приведён к нижнему регистру при сохранении.
/// </remarks>
public sealed class ArticleCardRepository : IArticleCardRepository
{
    private readonly IDbContextFactory<AbcpDbContext> _contextFactory;

    /// <summary>Создаёт репозиторий.</summary>
    public ArticleCardRepository(IDbContextFactory<AbcpDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ArticleCard>> GetAsync(
        IReadOnlyCollection<ArticleRef> articles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(articles);

        Dictionary<string, ArticleCard> result = new(StringComparer.Ordinal);
        if (articles.Count == 0)
        {
            return result;
        }

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        string[] matchKeys = articles
            .Select(article => article.MatchKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        List<ArticleCard> cards = await context.ArticleCards
            .AsNoTracking()
            .Where(card => matchKeys.Contains(card.MatchKey))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, ArticleCard> byExactKey = new(StringComparer.Ordinal);
        Dictionary<string, ArticleCard> byMatchKey = new(StringComparer.Ordinal);

        foreach (ArticleCard card in cards)
        {
            byExactKey[ArticleKey.Exact(card.Brand, card.Number)] = card;

            // Карточка, записанная ровно так же, как в заказе, точнее найденной
            // по сопоставительному ключу, поэтому она приоритетнее.
            byMatchKey.TryAdd(card.MatchKey, card);
        }

        foreach (ArticleRef article in articles)
        {
            if (byExactKey.TryGetValue(article.Key, out ArticleCard? exact))
            {
                result[article.Key] = exact;
                continue;
            }

            if (byMatchKey.TryGetValue(article.MatchKey, out ArticleCard? loose))
            {
                result[article.Key] = loose;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(
        IReadOnlyCollection<ArticleCard> cards,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cards);

        if (cards.Count == 0)
        {
            return;
        }

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (ArticleCard card in cards)
        {
            card.MatchKey = ArticleKey.Match(card.Brand, card.Number);
        }

        string[] matchKeys = cards
            .Select(card => card.MatchKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Dictionary<string, ArticleCard> existing = (await context.ArticleCards
                .Where(card => matchKeys.Contains(card.MatchKey))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(card => card.MatchKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (ArticleCard card in cards)
        {
            if (existing.TryGetValue(card.MatchKey, out ArticleCard? stored))
            {
                // Пустые значения не затирают сохранённые: один и тот же артикул
                // приходит из трёх источников (каталог, витрина, API) и повторяется
                // внутри выгрузки, причём изображение или штрихкод есть не в каждой записи.
                stored.NumberFix = card.NumberFix ?? stored.NumberFix;
                stored.Description = card.Description ?? stored.Description;
                stored.ImageName = card.ImageName ?? stored.ImageName;
                stored.PropertiesJson = card.PropertiesJson ?? stored.PropertiesJson;
                stored.Barcodes = card.Barcodes ?? stored.Barcodes;

                if (card.ImagesCount > 0)
                {
                    stored.ImagesCount = card.ImagesCount;
                }

                stored.NotFound = card.NotFound;
                stored.Source = card.Source;
                stored.SyncedAt = card.SyncedAt;
                continue;
            }

            context.ArticleCards.Add(card);
            existing[card.MatchKey] = card;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ArticleCard?> FindByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return null;
        }

        string code = barcode.Trim();

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Штрихкоды хранятся строкой через точку с запятой, поэтому запросом
        // отбираются кандидаты, а точное совпадение проверяется в памяти:
        // подстрока «4607030880082» нашлась бы и внутри более длинного кода.
        List<ArticleCard> candidates = await context.ArticleCards
            .AsNoTracking()
            .Where(card => card.Barcodes != null && EF.Functions.Like(card.Barcodes, "%" + code + "%"))
            .Take(50)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidates.FirstOrDefault(card => HasBarcode(card, code));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArticleCard>> SearchAsync(
        string query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        string text = query.Trim();

        // Ключ сопоставления даёт поиск без учёта разделителей: «ADW-0855»
        // находит карточку, записанную как «ADW0855».
        string matchKey = ArticleKey.Match(string.Empty, text).TrimStart('|');

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.ArticleCards
            .AsNoTracking()
            .Where(card =>
                EF.Functions.Like(card.MatchKey, "%" + matchKey + "%")
                || EF.Functions.Like(card.Number, "%" + text + "%")
                || EF.Functions.Like(card.Brand, "%" + text + "%")
                || (card.Description != null && EF.Functions.Like(card.Description, "%" + text + "%")))
            .OrderBy(card => card.Brand)
            .ThenBy(card => card.Number)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Проверяет, что штрихкод указан у карточки целиком, а не как часть другого.
    /// </summary>
    internal static bool HasBarcode(ArticleCard card, string barcode) =>
        card.Barcodes is { Length: > 0 } stored
        && stored
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(code => string.Equals(code, barcode, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Проставляет сопоставительный ключ карточкам, сохранённым до его появления.
    /// </summary>
    /// <remarks>
    /// Вычислить ключ в миграции нельзя: убрать все знаки, кроме букв и цифр,
    /// средствами SQLite нечем. Поэтому разовый проход делается кодом при запуске.
    /// </remarks>
    /// <returns>Сколько карточек обновлено.</returns>
    public async Task<int> BackfillMatchKeysAsync(CancellationToken cancellationToken = default)
    {
        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ArticleCard> pending = await context.ArticleCards
            .Where(card => card.MatchKey == string.Empty)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return 0;
        }

        foreach (ArticleCard card in pending)
        {
            card.MatchKey = ArticleKey.Match(card.Brand, card.Number);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return pending.Count;
    }
}
