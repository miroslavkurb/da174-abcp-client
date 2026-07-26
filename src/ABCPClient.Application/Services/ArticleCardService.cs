using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Exceptions;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Application.Services;

/// <summary>
/// Карточки товаров с локальным кэшем и ограничением частоты обращений к API.
/// </summary>
/// <remarks>
/// API ограничивает количество запросов в минуту, час и сутки: при исчерпании лимита
/// возвращается ошибка 303 и обращения блокируются. Поэтому:
/// карточка каждого артикула запрашивается один раз и сохраняется в базе;
/// расход лимита считается по всем трём окнам и хранится в базе, а не в памяти,
/// потому что перезапуск приложения счётчики на стороне API не обнуляет;
/// после ошибки 303 обращения приостанавливаются на время остывания,
/// а уже полученные карточки всё равно показываются.
///
/// Источники перебираются по возрастанию цены:
/// <list type="number">
/// <item>локальный кэш;</item>
/// <item>витрина магазина (<see cref="IStorefrontArticleSource"/>) — обычный сайт,
/// лимит API не расходует, и знает детали под заказ;</item>
/// <item>API — последним и дозированно.</item>
/// </list>
/// Кэш заранее заполняется импортом каталога (<see cref="ICatalogImporter"/>),
/// но в выгрузке только собственное наличие, поэтому витрина обязательна:
/// на живых данных из 343 артикулов в заказах в каталоге нашлись 53.
/// </remarks>
public sealed class ArticleCardService : IArticleCardService
{
    private readonly IAbcpApiClient _api;
    private readonly IArticleCardRepository _repository;
    private readonly IAbcpSettingsProvider _settings;
    private readonly IAppSettingsStore _store;
    private readonly IStorefrontArticleSource _storefront;
    private readonly ILogger<ArticleCardService> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Создаёт службу карточек товаров.</summary>
    public ArticleCardService(
        IAbcpApiClient api,
        IArticleCardRepository repository,
        IAbcpSettingsProvider settings,
        IAppSettingsStore store,
        IStorefrontArticleSource storefront,
        ILogger<ArticleCardService> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(storefront);
        ArgumentNullException.ThrowIfNull(logger);

        _api = api;
        _repository = repository;
        _settings = settings;
        _store = store;
        _storefront = storefront;
        _logger = logger;
    }

    /// <summary>
    /// Источник времени. Отдельным свойством — чтобы тесты не ждали реальных минут.
    /// </summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>
    /// Пауза между запросами в пределах разрешённой частоты.
    /// </summary>
    internal TimeSpan RequestSpacing { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <inheritdoc />
    public async Task<ArticleCardsResult> GetCardsAsync(
        IReadOnlyCollection<ArticleRef> articles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(articles);

        ArticleRef[] unique = articles
            .Where(article => !string.IsNullOrWhiteSpace(article.Brand) && !string.IsNullOrWhiteSpace(article.Number))
            .GroupBy(article => article.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        if (unique.Length == 0)
        {
            return new ArticleCardsResult(
                new Dictionary<string, ArticleCard>(StringComparer.Ordinal),
                0,
                0,
                0,
                0,
                false);
        }

        Dictionary<string, ArticleCard> result = new(
            await _repository.GetAsync(unique, cancellationToken).ConfigureAwait(false),
            StringComparer.Ordinal);

        int fromCache = result.Count;

        ArticleRef[] missing = unique
            .Where(article => !result.ContainsKey(article.Key))
            .ToArray();

        if (missing.Length == 0)
        {
            return new ArticleCardsResult(result, fromCache, 0, 0, 0, false);
        }

        SyncOptions options = await _settings.GetSyncOptionsAsync(cancellationToken).ConfigureAwait(false);

        List<ArticleCard> fetched = [];

        // Витрина идёт раньше API: это обычный сайт магазина, лимит вызовов API
        // её страницы не расходуют, и деталь под заказ там есть, а в каталоге нет.
        int fromStorefront = await FillFromStorefrontAsync(missing, result, fetched, cancellationToken)
            .ConfigureAwait(false);

        if (fromStorefront > 0)
        {
            missing = missing.Where(article => !result.ContainsKey(article.Key)).ToArray();
        }

        ApiFetchOutcome api = missing.Length == 0
            ? ApiFetchOutcome.Nothing
            : await FillFromApiAsync(missing, result, fetched, options, cancellationToken).ConfigureAwait(false);

        if (fetched.Count > 0)
        {
            await _repository.UpsertAsync(fetched, cancellationToken).ConfigureAwait(false);
        }

        if (api.NotRequested > 0 && api.ExhaustedLimit is not null)
        {
            _logger.LogInformation(
                "Достигнут собственный лимит запросов карточек ({Limit}); отложено {NotRequested}",
                api.ExhaustedLimit,
                api.NotRequested);
        }

        _logger.LogInformation(
            "Карточки товаров: из кэша {FromCache}, с витрины {FromStorefront}, из API {Fetched}, "
                + "отложено {NotRequested}",
            fromCache,
            fromStorefront,
            api.Fetched,
            api.NotRequested);

        return new ArticleCardsResult(
            result,
            fromCache,
            api.Fetched,
            fromStorefront,
            api.NotRequested,
            api.RateLimited);
    }

    /// <summary>
    /// Добирает карточки со страниц витрины магазина.
    /// </summary>
    /// <remarks>
    /// Лимит вызовов API эти обращения не расходуют, поэтому источник пробуется
    /// до API и без оглядки на счётчики. Пустой ответ (витрина такого товара
    /// не знает) не кэшируется: карточку ещё может отдать API.
    /// </remarks>
    /// <returns>Сколько карточек получено.</returns>
    private async Task<int> FillFromStorefrontAsync(
        IReadOnlyList<ArticleRef> missing,
        Dictionary<string, ArticleCard> result,
        List<ArticleCard> fetched,
        CancellationToken cancellationToken)
    {
        if (!await _storefront.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        int found = 0;

        foreach (ArticleRef article in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();

            StorefrontArticle? page;
            try
            {
                page = await _storefront.GetAsync(article, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Витрина — вспомогательный источник: её сбой не должен мешать
                // ни показу заказа, ни обращению к API.
                _logger.LogDebug(exception, "Витрина не ответила по {Brand} {Number}", article.Brand, article.Number);
                continue;
            }

            if (page is null || page.IsEmpty)
            {
                continue;
            }

            ArticleCard card = new()
            {
                Brand = article.Brand,
                Number = article.Number,
                Description = page.Description,
                ImageName = page.ImageUrl,
                ImagesCount = page.ImageUrl is null ? 0 : 1,
                NotFound = false,
                Source = ArticleCardSource.Storefront,
                SyncedAt = Time.GetUtcNow().LocalDateTime,
            };

            fetched.Add(card);
            result[article.Key] = card;
            found++;
        }

        return found;
    }

    /// <summary>
    /// Добирает недостающие карточки из API с соблюдением лимитов.
    /// </summary>
    private async Task<ApiFetchOutcome> FillFromApiAsync(
        IReadOnlyList<ArticleRef> missing,
        Dictionary<string, ArticleCard> result,
        List<ArticleCard> fetched,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        int fromApi = 0;
        int notRequested = 0;
        bool rateLimited = false;
        string? exhaustedLimit = null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ArticleCardUsage usage = await LoadUsageAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset? blockedUntil = await LoadBlockedUntilAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                foreach (ArticleRef article in missing)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (blockedUntil is { } until && Time.GetUtcNow() < until)
                    {
                        notRequested++;
                        rateLimited = true;
                        continue;
                    }

                    if (!TryReserve(usage, options, out exhaustedLimit))
                    {
                        // Собственный лимит на окно выбран. Это не отказ API:
                        // остальные карточки догрузятся при следующем открытии заказа.
                        notRequested++;
                        continue;
                    }

                    if (fromApi > 0 && RequestSpacing > TimeSpan.Zero)
                    {
                        // Ровный темп вместо очереди запросов подряд.
                        await Task.Delay(RequestSpacing, Time, cancellationToken).ConfigureAwait(false);
                    }

                    try
                    {
                        IReadOnlyList<ArticleInfoDto> cards = await _api
                            .GetArticlesInfoAsync([article], cancellationToken)
                            .ConfigureAwait(false);

                        ArticleCard card = cards.Count > 0
                            ? ToEntity(article, cards[0])
                            : NotFoundCard(article);

                        fetched.Add(card);
                        result[article.Key] = card;
                        fromApi++;
                    }
                    catch (AbcpApiException exception) when (exception.ErrorCode == AbcpErrorCodes.ResourceLocked)
                    {
                        // Лимит исчерпан: дальше не долбим API, иначе блокировка продлится.
                        blockedUntil = Time.GetUtcNow()
                            + TimeSpan.FromMinutes(Math.Clamp(options.ArticleCardCooldownMinutes, 1, 240));

                        rateLimited = true;
                        notRequested++;

                        _logger.LogWarning(
                            "API ограничил обращения (ошибка 303). Загрузка карточек приостановлена до {BlockedUntil:HH:mm:ss}. "
                                + "Расход: {Minute} за минуту, {Hour} за час, {Day} за сутки",
                            blockedUntil.Value.ToLocalTime(),
                            usage.MinuteCount,
                            usage.HourCount,
                            usage.DayCount);
                    }
                }
            }
            finally
            {
                // Счётчики сохраняются даже при отмене: запросы уже ушли в API.
                await SaveUsageAsync(usage, blockedUntil, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        return new ApiFetchOutcome(fromApi, notRequested, rateLimited, exhaustedLimit);
    }

    /// <summary>Итог обращения к API за карточками.</summary>
    /// <param name="Fetched">Сколько карточек получено.</param>
    /// <param name="NotRequested">Сколько деталей осталось без запроса.</param>
    /// <param name="RateLimited">API ответил ошибкой 303 или пауза после неё ещё идёт.</param>
    /// <param name="ExhaustedLimit">Какое собственное окно лимита исчерпано.</param>
    private sealed record ApiFetchOutcome(int Fetched, int NotRequested, bool RateLimited, string? ExhaustedLimit)
    {
        /// <summary>К API не обращались.</summary>
        public static ApiFetchOutcome Nothing { get; } = new(0, 0, false, null);
    }

    /// <summary>
    /// Занимает место в окнах «минута», «час» и «сутки».
    /// </summary>
    /// <param name="usage">Счётчики расхода.</param>
    /// <param name="options">Действующие ограничения.</param>
    /// <param name="exhausted">Какое окно исчерпано, если место занять не удалось.</param>
    /// <returns><c>false</c>, если хотя бы одно из окон исчерпано.</returns>
    private bool TryReserve(ArticleCardUsage usage, SyncOptions options, out string? exhausted)
    {
        DateTimeOffset now = Time.GetUtcNow();
        usage.Roll(now);

        int perMinute = Math.Clamp(options.ArticleCardRequestsPerMinute, 1, 600);
        int perHour = Math.Clamp(options.ArticleCardRequestsPerHour, perMinute, 20_000);
        int perDay = Math.Clamp(options.ArticleCardRequestsPerDay, perHour, 200_000);

        if (usage.MinuteCount >= perMinute)
        {
            exhausted = "в минуту";
            return false;
        }

        if (usage.HourCount >= perHour)
        {
            exhausted = "в час";
            return false;
        }

        if (usage.DayCount >= perDay)
        {
            exhausted = "в сутки";
            return false;
        }

        usage.MinuteCount++;
        usage.HourCount++;
        usage.DayCount++;

        exhausted = null;
        return true;
    }

    private async Task<ArticleCardUsage> LoadUsageAsync(CancellationToken cancellationToken)
    {
        string? raw = await _store.GetAsync(AppSettingKeys.ArticleCardUsage, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ArticleCardUsage();
        }

        try
        {
            return JsonSerializer.Deserialize<ArticleCardUsage>(raw) ?? new ArticleCardUsage();
        }
        catch (JsonException)
        {
            // Испорченное значение не должно мешать работе: считаем расход заново.
            return new ArticleCardUsage();
        }
    }

    private async Task<DateTimeOffset?> LoadBlockedUntilAsync(CancellationToken cancellationToken)
    {
        string? raw = await _store
            .GetAsync(AppSettingKeys.ArticleCardBlockedUntil, cancellationToken)
            .ConfigureAwait(false);

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private async Task SaveUsageAsync(
        ArticleCardUsage usage,
        DateTimeOffset? blockedUntil,
        CancellationToken cancellationToken)
    {
        await _store
            .SetAsync(
                AppSettingKeys.ArticleCardUsage,
                JsonSerializer.Serialize(usage),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _store
            .SetAsync(
                AppSettingKeys.ArticleCardBlockedUntil,
                blockedUntil?.ToString("O", CultureInfo.InvariantCulture),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private ArticleCard ToEntity(ArticleRef article, ArticleInfoDto dto) => new()
    {
        Brand = article.Brand,
        Number = article.Number,
        NumberFix = string.IsNullOrWhiteSpace(dto.Number) ? null : dto.Number,
        Description = dto.Description,
        ImageName = dto.Images
            .OrderBy(image => image.Order)
            .Select(image => image.Name)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
        ImagesCount = dto.ImagesCount,
        PropertiesJson = dto.Properties.Count == 0 ? null : JsonSerializer.Serialize(dto.Properties),
        NotFound = false,
        Source = ArticleCardSource.Api,
        SyncedAt = Time.GetUtcNow().LocalDateTime,
    };

    private ArticleCard NotFoundCard(ArticleRef article) => new()
    {
        Brand = article.Brand,
        Number = article.Number,
        NotFound = true,
        Source = ArticleCardSource.Api,
        SyncedAt = Time.GetUtcNow().LocalDateTime,
    };
}

/// <summary>
/// Расход лимита запросов карточек по окнам «минута», «час» и «сутки».
/// </summary>
/// <remarks>
/// Хранится в настройках приложения в виде JSON. Границы окон выровнены по началу
/// минуты, часа и суток — так счётчик ближе всего к тому, как считает сам API.
/// </remarks>
internal sealed class ArticleCardUsage
{
    /// <summary>Начало текущей минуты.</summary>
    [JsonPropertyName("minuteStart")]
    public DateTimeOffset MinuteStart { get; set; }

    /// <summary>Запросов за текущую минуту.</summary>
    [JsonPropertyName("minute")]
    public int MinuteCount { get; set; }

    /// <summary>Начало текущего часа.</summary>
    [JsonPropertyName("hourStart")]
    public DateTimeOffset HourStart { get; set; }

    /// <summary>Запросов за текущий час.</summary>
    [JsonPropertyName("hour")]
    public int HourCount { get; set; }

    /// <summary>Начало текущих суток.</summary>
    [JsonPropertyName("dayStart")]
    public DateTimeOffset DayStart { get; set; }

    /// <summary>Запросов за текущие сутки.</summary>
    [JsonPropertyName("day")]
    public int DayCount { get; set; }

    /// <summary>
    /// Сбрасывает счётчики окон, которые уже закончились.
    /// </summary>
    /// <param name="now">Текущий момент.</param>
    public void Roll(DateTimeOffset now)
    {
        DateTimeOffset minute = Floor(now, TimeSpan.FromMinutes(1));
        DateTimeOffset hour = Floor(now, TimeSpan.FromHours(1));
        DateTimeOffset day = Floor(now, TimeSpan.FromDays(1));

        // Время назад (перевод часов, правка системных часов) тоже начинает окно заново:
        // иначе счётчик замер бы до возвращения к прежнему моменту.
        if (MinuteStart != minute)
        {
            MinuteStart = minute;
            MinuteCount = 0;
        }

        if (HourStart != hour)
        {
            HourStart = hour;
            HourCount = 0;
        }

        if (DayStart != day)
        {
            DayStart = day;
            DayCount = 0;
        }
    }

    private static DateTimeOffset Floor(DateTimeOffset value, TimeSpan unit) =>
        new(value.UtcTicks - (value.UtcTicks % unit.Ticks), TimeSpan.Zero);
}
