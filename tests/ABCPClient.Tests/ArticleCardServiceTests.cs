using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Exceptions;
using ABCPClient.Application.Interfaces;
using ABCPClient.Application.Services;
using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет кэш карточек товаров и защиту от исчерпания лимита запросов API.
/// </summary>
public sealed class ArticleCardServiceTests
{
    private static ArticleCardService CreateService(
        FakeCardApi api,
        InMemoryCardRepository repository,
        int perMinute = 20,
        int cooldownMinutes = 15,
        int perHour = 300,
        int perDay = 1500,
        IAppSettingsStore? store = null,
        IStorefrontArticleSource? storefront = null) =>
        new(
            api,
            repository,
            new FixedSettings(new SyncOptions
            {
                ArticleCardRequestsPerMinute = perMinute,
                ArticleCardRequestsPerHour = perHour,
                ArticleCardRequestsPerDay = perDay,
                ArticleCardCooldownMinutes = cooldownMinutes,
            }),
            store ?? new InMemorySettingsStore(),
            storefront ?? new DisabledStorefront(),
            NullLogger<ArticleCardService>.Instance)
        {
            // Без паузы между запросами, чтобы тесты не ждали.
            RequestSpacing = TimeSpan.Zero,
        };

    private static ArticleInfoDto Card(string brand, string number, string? image = "pic.jpeg") => new()
    {
        Brand = brand,
        Number = number.Replace("-", string.Empty, StringComparison.Ordinal),
        OuterNumber = number,
        Description = "Описание",
        ImagesCount = image is null ? 0 : 1,
        Images = image is null ? [] : [new ArticleImageDto { Name = image, Order = 0 }],
    };

    [Fact]
    public async Task Cards_are_fetched_once_and_then_taken_from_cache()
    {
        FakeCardApi api = new();
        api.Cards["acq|adw-0855"] = Card("ACQ", "ADW-0855");

        InMemoryCardRepository repository = new();
        ArticleCardService service = CreateService(api, repository);

        ArticleCardsResult first = await service.GetCardsAsync([new ArticleRef("ACQ", "ADW-0855")]);

        Assert.Equal(1, first.FetchedFromApi);
        Assert.Equal(0, first.FromCache);
        Assert.Equal("pic.jpeg", first.Cards.Values.Single().ImageName);
        Assert.Single(api.Requests);

        // Второй раз карточка берётся из базы: лимит вызовов API не расходуется.
        ArticleCardsResult second = await service.GetCardsAsync([new ArticleRef("ACQ", "ADW-0855")]);

        Assert.Equal(0, second.FetchedFromApi);
        Assert.Equal(1, second.FromCache);
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task Missing_card_is_remembered_as_not_found()
    {
        FakeCardApi api = new();
        InMemoryCardRepository repository = new();
        ArticleCardService service = CreateService(api, repository);

        await service.GetCardsAsync([new ArticleRef("НетТакого", "000")]);
        ArticleCardsResult second = await service.GetCardsAsync([new ArticleRef("НетТакого", "000")]);

        // Повторно API не спрашиваем: отсутствие карточки тоже сохранено.
        Assert.Single(api.Requests);
        Assert.Equal(1, second.FromCache);
        Assert.True(second.Cards.Values.Single().NotFound);
    }

    [Fact]
    public async Task Rate_limit_error_stops_further_requests()
    {
        FakeCardApi api = new();
        api.Cards["a|1"] = Card("A", "1");
        api.FailWithRateLimitAfter = 1;

        InMemoryCardRepository repository = new();
        ArticleCardService service = CreateService(api, repository);

        ArticleCardsResult result = await service.GetCardsAsync(
        [
            new ArticleRef("A", "1"),
            new ArticleRef("B", "2"),
            new ArticleRef("C", "3"),
            new ArticleRef("D", "4"),
        ]);

        Assert.True(result.RateLimited);
        Assert.Equal(1, result.FetchedFromApi);

        // После ошибки 303 обращения прекращаются: второй запрос был последним.
        Assert.Equal(2, api.Requests.Count);
        Assert.Equal(3, result.NotRequested);

        // Успешно полученная карточка сохранена и остаётся доступной.
        Assert.Single(repository.Stored);
        Assert.True(result.Cards.ContainsKey(new ArticleRef("A", "1").Key));
    }

    [Fact]
    public async Task Cooldown_blocks_requests_until_it_expires()
    {
        FakeCardApi api = new();
        api.FailWithRateLimitAfter = 0;

        InMemoryCardRepository repository = new();
        FakeTime time = new(DateTimeOffset.Parse("2026-07-25T10:00:00Z", null));

        ArticleCardService service = CreateService(api, repository, cooldownMinutes: 15);
        service.Time = time;

        await service.GetCardsAsync([new ArticleRef("A", "1")]);
        Assert.Single(api.Requests);

        // Пока идёт остывание, к API не обращаемся вовсе.
        ArticleCardsResult during = await service.GetCardsAsync([new ArticleRef("B", "2")]);
        Assert.Single(api.Requests);
        Assert.True(during.RateLimited);

        // После остывания запросы возобновляются.
        time.Advance(TimeSpan.FromMinutes(16));
        api.FailWithRateLimitAfter = null;
        api.Cards["b|2"] = Card("B", "2");

        ArticleCardsResult after = await service.GetCardsAsync([new ArticleRef("B", "2")]);

        Assert.Equal(2, api.Requests.Count);
        Assert.Equal(1, after.FetchedFromApi);
    }

    [Fact]
    public async Task Requests_are_capped_per_minute()
    {
        FakeCardApi api = new();
        for (int index = 1; index <= 5; index++)
        {
            api.Cards[$"b|{index}"] = Card("B", index.ToString());
        }

        InMemoryCardRepository repository = new();
        FakeTime time = new(DateTimeOffset.Parse("2026-07-25T10:00:00Z", null));

        ArticleCardService service = CreateService(api, repository, perMinute: 2);
        service.Time = time;

        ArticleCardsResult result = await service.GetCardsAsync(
            Enumerable.Range(1, 5).Select(index => new ArticleRef("B", index.ToString())).ToArray());

        // В окне разрешено два обращения, остальные отложены.
        Assert.Equal(2, api.Requests.Count);
        Assert.Equal(2, result.FetchedFromApi);
        Assert.Equal(3, result.NotRequested);
        Assert.False(result.RateLimited);

        // Через минуту окно освобождается.
        time.Advance(TimeSpan.FromMinutes(1));
        ArticleCardsResult next = await service.GetCardsAsync(
            Enumerable.Range(1, 5).Select(index => new ArticleRef("B", index.ToString())).ToArray());

        Assert.Equal(4, api.Requests.Count);
        Assert.Equal(2, next.FromCache);
    }

    [Fact]
    public async Task Duplicate_articles_cost_one_request()
    {
        FakeCardApi api = new();
        api.Cards["acq|adw-0855"] = Card("ACQ", "ADW-0855");

        ArticleCardService service = CreateService(api, new InMemoryCardRepository());

        await service.GetCardsAsync(
        [
            new ArticleRef("ACQ", "ADW-0855"),
            new ArticleRef("acq", "adw-0855"),
            new ArticleRef("ACQ", "ADW-0855"),
        ]);

        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task Hourly_limit_is_respected_even_when_minute_window_is_free()
    {
        FakeCardApi api = new();
        for (int index = 1; index <= 6; index++)
        {
            api.Cards[$"b|{index}"] = Card("B", index.ToString());
        }

        InMemoryCardRepository repository = new();
        InMemorySettingsStore store = new();
        FakeTime time = new(DateTimeOffset.Parse("2026-07-25T10:00:00Z", null));

        ArticleCardService service = CreateService(api, repository, perMinute: 2, perHour: 3, store: store);
        service.Time = time;

        await service.GetCardsAsync([new ArticleRef("B", "1"), new ArticleRef("B", "2")]);
        Assert.Equal(2, api.Requests.Count);

        // Новая минута окно в минуту освобождает, но часовой лимит остаётся общим.
        time.Advance(TimeSpan.FromMinutes(1));
        ArticleCardsResult second = await service.GetCardsAsync(
            [new ArticleRef("B", "3"), new ArticleRef("B", "4")]);

        Assert.Equal(3, api.Requests.Count);
        Assert.Equal(1, second.NotRequested);

        // Через час счётчик обнуляется.
        time.Advance(TimeSpan.FromHours(1));
        await service.GetCardsAsync([new ArticleRef("B", "5")]);

        Assert.Equal(4, api.Requests.Count);
    }

    [Fact]
    public async Task Usage_and_cooldown_survive_restart()
    {
        FakeCardApi api = new();
        api.FailWithRateLimitAfter = 0;

        InMemorySettingsStore store = new();
        FakeTime time = new(DateTimeOffset.Parse("2026-07-25T10:00:00Z", null));

        ArticleCardService first = CreateService(api, new InMemoryCardRepository(), store: store);
        first.Time = time;

        await first.GetCardsAsync([new ArticleRef("A", "1")]);
        Assert.Single(api.Requests);

        // Новый экземпляр службы — как после перезапуска приложения.
        // Счётчики и остывание берутся из базы, а не начинаются заново.
        ArticleCardService restarted = CreateService(api, new InMemoryCardRepository(), store: store);
        restarted.Time = time;

        ArticleCardsResult afterRestart = await restarted.GetCardsAsync([new ArticleRef("A", "2")]);

        Assert.Single(api.Requests);
        Assert.True(afterRestart.RateLimited);
        Assert.NotNull(store.Values[AppSettingKeys.ArticleCardUsage]);
    }

    [Fact]
    public async Task Restart_does_not_reset_the_minute_window()
    {
        FakeCardApi api = new();
        api.Cards["b|1"] = Card("B", "1");
        api.Cards["b|2"] = Card("B", "2");

        InMemorySettingsStore store = new();
        FakeTime time = new(DateTimeOffset.Parse("2026-07-25T10:00:30Z", null));

        ArticleCardService first = CreateService(api, new InMemoryCardRepository(), perMinute: 1, store: store);
        first.Time = time;

        await first.GetCardsAsync([new ArticleRef("B", "1")]);
        Assert.Single(api.Requests);

        ArticleCardService restarted = CreateService(api, new InMemoryCardRepository(), perMinute: 1, store: store);
        restarted.Time = time;

        ArticleCardsResult result = await restarted.GetCardsAsync([new ArticleRef("B", "2")]);

        // Лимит выбран в этой же минуте: перезапуск его не возвращает.
        Assert.Single(api.Requests);
        Assert.Equal(1, result.NotRequested);
    }

    [Fact]
    public async Task Storefront_is_used_before_api()
    {
        FakeCardApi api = new();
        api.Cards["b|2"] = Card("B", "2");

        FakeStorefront storefront = new();
        storefront.Pages["b|1"] = new StorefrontArticle(
            "https://imgcdn.abcp.ru/p/full/09601d0c.jpeg",
            "Датчик кислорода");

        InMemoryCardRepository repository = new();
        ArticleCardService service = CreateService(api, repository, storefront: storefront);

        ArticleCardsResult result = await service.GetCardsAsync(
            [new ArticleRef("B", "1"), new ArticleRef("B", "2")]);

        // Первая деталь нашлась на витрине, поэтому в API ушла только вторая.
        Assert.Equal(1, result.FetchedFromStorefront);
        Assert.Equal(1, result.FetchedFromApi);
        Assert.Equal(new ArticleRef("B", "2"), Assert.Single(api.Requests));

        ArticleCard card = repository.Stored["b|1"];
        Assert.Equal("https://imgcdn.abcp.ru/p/full/09601d0c.jpeg", card.ImageName);
        Assert.Equal("Датчик кислорода", card.Description);
        Assert.Equal(ArticleCardSource.Storefront, card.Source);

        // На первом заходе витрину спросили про обе детали: вторую она не знает.
        Assert.Equal(2, storefront.Requests.Count);

        // Повторный запрос идёт только в кэш — витрину больше не тревожим.
        ArticleCardsResult second = await service.GetCardsAsync([new ArticleRef("B", "1")]);
        Assert.Equal(1, second.FromCache);
        Assert.Equal(0, second.FetchedFromStorefront);
        Assert.Equal(2, storefront.Requests.Count);
    }

    [Fact]
    public async Task Storefront_works_while_api_is_blocked()
    {
        FakeCardApi api = new();
        api.FailWithRateLimitAfter = 0;

        FakeStorefront storefront = new();
        storefront.Pages["b|2"] = new StorefrontArticle("https://cdn/p.jpeg", "Втулка");

        InMemorySettingsStore store = new();
        FakeTime time = new(DateTimeOffset.Parse("2026-07-25T10:00:00Z", null));

        ArticleCardService service = CreateService(
            api,
            new InMemoryCardRepository(),
            store: store,
            storefront: storefront);
        service.Time = time;

        // Первый запрос упирается в 303 и включает паузу.
        await service.GetCardsAsync([new ArticleRef("B", "1")]);

        // Пока API в паузе, витрина продолжает отдавать карточки.
        ArticleCardsResult during = await service.GetCardsAsync([new ArticleRef("B", "2")]);

        Assert.Equal(1, during.FetchedFromStorefront);
        Assert.Equal(0, during.FetchedFromApi);
        Assert.False(during.RateLimited);
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task Empty_storefront_page_falls_through_to_api()
    {
        FakeCardApi api = new();
        api.Cards["b|1"] = Card("B", "1");

        FakeStorefront storefront = new();
        storefront.Pages["b|1"] = new StorefrontArticle(null, null);

        ArticleCardService service = CreateService(
            api,
            new InMemoryCardRepository(),
            storefront: storefront);

        ArticleCardsResult result = await service.GetCardsAsync([new ArticleRef("B", "1")]);

        Assert.Equal(0, result.FetchedFromStorefront);
        Assert.Equal(1, result.FetchedFromApi);
    }

    [Fact]
    public void Properties_json_is_flattened()
    {
        const string json = """
        { "Вязкость": "5W-40", "Допуски": [ "MB 229.3", "VW 502 00" ], "Число": 4, "Флаг": true }
        """;

        Dictionary<string, string> properties = ArticleCardProperties.Flatten(json)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        Assert.Equal("5W-40", properties["Вязкость"]);
        Assert.Equal("MB 229.3, VW 502 00", properties["Допуски"]);
        Assert.Equal("4", properties["Число"]);
        Assert.Equal("да", properties["Флаг"]);

        Assert.Empty(ArticleCardProperties.Flatten(null));
        Assert.Empty(ArticleCardProperties.Flatten("не json"));
    }

    /// <summary>Источник времени с ручным управлением.</summary>
    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTime(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    /// <summary>Клиент API, отдающий заготовленные карточки.</summary>
    private sealed class FakeCardApi : IAbcpApiClient
    {
        public Dictionary<string, ArticleInfoDto> Cards { get; } = new(StringComparer.Ordinal);

        public List<ArticleRef> Requests { get; } = [];

        /// <summary>После какого числа успешных запросов начинать отвечать ошибкой 303.</summary>
        public int? FailWithRateLimitAfter { get; set; }

        private int _successes;

        public Task<IReadOnlyList<ArticleInfoDto>> GetArticlesInfoAsync(
            IReadOnlyCollection<ArticleRef> articles,
            CancellationToken cancellationToken = default)
        {
            ArticleRef article = articles.Single();
            Requests.Add(article);

            if (FailWithRateLimitAfter is { } limit && _successes >= limit)
            {
                throw new AbcpApiException(
                    "The resource is blocked.",
                    null,
                    AbcpErrorCodes.ResourceLocked,
                    "articles/info");
            }

            _successes++;

            return Task.FromResult<IReadOnlyList<ArticleInfoDto>>(
                Cards.TryGetValue(article.Key, out ArticleInfoDto? card) ? [card] : []);
        }

        public Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrderPage([], 0));

        public Task<int> GetOrdersCountAsync(OrderQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<OrderDto?> GetOrderAsync(string number, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrderDto?>(null);

        public Task<IReadOnlyList<OrderStatusDto>> GetStatusesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderStatusDto>>([]);

        public Task<IReadOnlyDictionary<long, IReadOnlyList<PositionStatusHistoryDto>>> GetStatusHistoryAsync(
            IReadOnlyCollection<long> positionIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<long, IReadOnlyList<PositionStatusHistoryDto>>>(
                new Dictionary<long, IReadOnlyList<PositionStatusHistoryDto>>());

        public Task<ConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionCheckResult(true, "ок"));
    }

    /// <summary>Кэш карточек в памяти.</summary>
    private sealed class InMemoryCardRepository : IArticleCardRepository
    {
        public Dictionary<string, ArticleCard> Stored { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyDictionary<string, ArticleCard>> GetAsync(
            IReadOnlyCollection<ArticleRef> articles,
            CancellationToken cancellationToken = default)
        {
            Dictionary<string, ArticleCard> found = new(StringComparer.Ordinal);

            foreach (ArticleRef article in articles)
            {
                if (Stored.TryGetValue(article.Key, out ArticleCard? card))
                {
                    found[article.Key] = card;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, ArticleCard>>(found);
        }

        public Task UpsertAsync(
            IReadOnlyCollection<ArticleCard> cards,
            CancellationToken cancellationToken = default)
        {
            foreach (ArticleCard card in cards)
            {
                Stored[new ArticleRef(card.Brand, card.Number).Key] = card;
            }

            return Task.CompletedTask;
        }

        public Task<ArticleCard?> FindByBarcodeAsync(
            string barcode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.Values.FirstOrDefault(card =>
                card.Barcodes is { Length: > 0 } codes
                && codes.Split(';').Contains(barcode, StringComparer.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<ArticleCard>> SearchAsync(
            string query,
            int limit = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArticleCard>>(Stored.Values
                .Where(card => card.Number.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToArray());
    }

    /// <summary>Витрина не настроена: источник выключен.</summary>
    private sealed class DisabledStorefront : IStorefrontArticleSource
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<StorefrontArticle?> GetAsync(
            ArticleRef article,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Выключенная витрина не должна запрашиваться");
    }

    /// <summary>Витрина, отдающая заготовленные страницы.</summary>
    private sealed class FakeStorefront : IStorefrontArticleSource
    {
        public Dictionary<string, StorefrontArticle> Pages { get; } = new(StringComparer.Ordinal);

        public List<ArticleRef> Requests { get; } = [];

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<StorefrontArticle?> GetAsync(
            ArticleRef article,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(article);

            return Task.FromResult(Pages.TryGetValue(article.Key, out StorefrontArticle? page) ? page : null);
        }
    }

    /// <summary>Хранилище настроек в памяти.</summary>
    private sealed class InMemorySettingsStore : IAppSettingsStore
    {
        public Dictionary<string, string?> Values { get; } = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.TryGetValue(key, out string? value) ? value : null);

        public Task SetAsync(
            string key,
            string? value,
            bool protect = false,
            CancellationToken cancellationToken = default)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string?>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string?>>(Values);

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.Remove(key));
    }

    /// <summary>Настройки с фиксированными значениями.</summary>
    private sealed class FixedSettings : IAbcpSettingsProvider
    {
        private readonly SyncOptions _sync;

        public FixedSettings(SyncOptions sync) => _sync = sync;

        public Task<AbcpApiOptions> GetApiOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AbcpApiOptions());

        public Task<SyncOptions> GetSyncOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_sync);

        public Task<CatalogOptions> GetCatalogOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogOptions());

        public Task<UpdateOptions> GetUpdateOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateOptions());
    }
}
