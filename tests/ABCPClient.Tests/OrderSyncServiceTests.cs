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
/// Проверяет логику инкрементальной синхронизации: окно выборки, пагинацию,
/// точку продолжения и запись журнала.
/// </summary>
public sealed class OrderSyncServiceTests
{
    private static OrderSyncService CreateService(
        FakeApiClient api,
        FakeOrderRepository orders,
        InMemorySettingsStore store,
        FakeSyncLogRepository log,
        AbcpApiOptions? apiOptions = null,
        SyncOptions? syncOptions = null) =>
        new(
            api,
            orders,
            new FakeStatusCatalogRepository(),
            log,
            store,
            new FakeSettingsProvider(
                apiOptions ?? new AbcpApiOptions
                {
                    BaseUrl = "https://demo.public.api.abcp.ru",
                    Login = "api-admin",
                    PasswordMd5 = new string('a', 32),
                    PageSize = 2,
                },
                syncOptions ?? new SyncOptions { OverlapMinutes = 5, InitialSyncDays = 30 }),
            NullLogger<OrderSyncService>.Instance);

    private static OrderDto Order(string number, DateTime updated) => new()
    {
        Number = number,
        DateUpdated = updated,
        Sum = 100m,
    };

    [Fact]
    public async Task Skips_when_api_is_not_configured()
    {
        FakeApiClient api = new();
        FakeSyncLogRepository log = new();

        OrderSyncService service = CreateService(
            api,
            new FakeOrderRepository(),
            new InMemorySettingsStore(),
            log,
            apiOptions: new AbcpApiOptions());

        SyncResult result = await service.SyncAsync();

        Assert.Equal(SyncOutcome.Skipped, result.Outcome);
        Assert.Empty(api.Queries);

        // Пропуск тоже попадает в журнал: иначе непонятно, почему нет данных.
        SyncLogEntry entry = Assert.Single(log.Entries);
        Assert.Equal(SyncOutcome.Skipped, entry.Outcome);
    }

    [Fact]
    public async Task First_run_uses_initial_depth_window()
    {
        FakeApiClient api = new();
        api.Pages.Add(new OrderPage([], 0));

        OrderSyncService service = CreateService(
            api,
            new FakeOrderRepository(),
            new InMemorySettingsStore(),
            new FakeSyncLogRepository(),
            syncOptions: new SyncOptions { InitialSyncDays = 10, OverlapMinutes = 5 });

        SyncResult result = await service.SyncAsync();

        Assert.True(result.IsSuccess);

        OrderQuery query = Assert.Single(api.Queries);
        Assert.NotNull(query.DateUpdatedStart);
        Assert.Equal(DateTime.Now.Date.AddDays(-10), query.DateUpdatedStart!.Value.Date);
    }

    [Fact]
    public async Task Window_is_taken_with_overlap_from_saved_watermark()
    {
        InMemorySettingsStore store = new();
        await store.SetAsync(AppSettingKeys.SyncLastSyncAt, "2026-07-25 09:00:00");

        FakeApiClient api = new();
        api.Pages.Add(new OrderPage([], 0));

        OrderSyncService service = CreateService(
            api,
            new FakeOrderRepository(),
            store,
            new FakeSyncLogRepository(),
            syncOptions: new SyncOptions { OverlapMinutes = 5 });

        await service.SyncAsync();

        OrderQuery query = Assert.Single(api.Queries);
        Assert.Equal(new DateTime(2026, 7, 25, 8, 55, 0), query.DateUpdatedStart);
    }

    [Fact]
    public async Task Reads_all_pages_and_deduplicates_orders()
    {
        FakeApiClient api = new();
        api.Pages.Add(new OrderPage(
            [Order("1", new DateTime(2026, 7, 25, 10, 0, 0)), Order("2", new DateTime(2026, 7, 25, 10, 5, 0))],
            3));
        api.Pages.Add(new OrderPage(
            [Order("2", new DateTime(2026, 7, 25, 10, 5, 0)), Order("3", new DateTime(2026, 7, 25, 10, 9, 0))],
            3));

        FakeOrderRepository orders = new();
        InMemorySettingsStore store = new();

        OrderSyncService service = CreateService(api, orders, store, new FakeSyncLogRepository());

        SyncResult result = await service.SyncAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.OrdersFetched);
        Assert.Equal(2, api.Queries.Count);
        Assert.Equal([0, 2], api.Queries.Select(query => query.Skip ?? 0).ToArray());
        Assert.Equal(["1", "2", "3"], orders.Upserted.Select(order => order.Number).ToArray());
    }

    [Fact]
    public async Task Watermark_is_saved_from_portal_time_of_latest_order()
    {
        FakeApiClient api = new();
        api.Pages.Add(new OrderPage(
            [Order("1", new DateTime(2026, 7, 25, 10, 0, 0)), Order("2", new DateTime(2026, 7, 25, 12, 30, 45))],
            2));

        InMemorySettingsStore store = new();

        OrderSyncService service = CreateService(api, new FakeOrderRepository(), store, new FakeSyncLogRepository());

        await service.SyncAsync();

        Assert.Equal("2026-07-25 12:30:45", await store.GetAsync(AppSettingKeys.SyncLastSyncAt));
    }

    [Fact]
    public async Task Watermark_stays_when_nothing_returned()
    {
        InMemorySettingsStore store = new();
        await store.SetAsync(AppSettingKeys.SyncLastSyncAt, "2026-07-25 09:00:00");

        FakeApiClient api = new();
        api.Pages.Add(new OrderPage([], 0));

        OrderSyncService service = CreateService(api, new FakeOrderRepository(), store, new FakeSyncLogRepository());

        await service.SyncAsync();

        Assert.Equal("2026-07-25 09:00:00", await store.GetAsync(AppSettingKeys.SyncLastSyncAt));
    }

    [Fact]
    public async Task Api_failure_is_reported_and_logged()
    {
        FakeApiClient api = new()
        {
            Failure = new AbcpApiException("Ошибка", null, AbcpErrorCodes.AccessDenied, "cp/orders"),
        };

        FakeSyncLogRepository log = new();

        OrderSyncService service = CreateService(api, new FakeOrderRepository(), new InMemorySettingsStore(), log);

        SyncResult result = await service.SyncAsync();

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Equal(AbcpErrorCodes.AccessDenied, result.ErrorCode);

        SyncLogEntry entry = Assert.Single(log.Entries);
        Assert.Equal(AbcpErrorCodes.AccessDenied, entry.ErrorCode);
        Assert.Equal(SyncOutcome.Failed, entry.Outcome);
    }

    [Fact]
    public async Task Sync_asks_api_for_deleted_orders()
    {
        FakeApiClient api = new();
        api.Pages.Add(new OrderPage([], 0));

        OrderSyncService service = CreateService(
            api,
            new FakeOrderRepository(),
            new InMemorySettingsStore(),
            new FakeSyncLogRepository());

        await service.SyncAsync();

        // Без withDeleted удаление заказа в панели управления выглядит как
        // «заказ перестал приходить», и он навсегда остаётся в локальной базе.
        Assert.True(Assert.Single(api.Queries).WithDeleted);
    }

    [Fact]
    public async Task Reconcile_marks_orders_deleted_in_portal()
    {
        FakeOrderRepository orders = new()
        {
            Active = [new ActiveOrderRef("100", new DateTime(2026, 1, 10)), new ActiveOrderRef("200", new DateTime(2026, 1, 12))],
        };

        OrderDto alive = Order("100", new DateTime(2026, 7, 25, 9, 0, 0));
        OrderDto removed = Order("200", new DateTime(2026, 7, 25, 9, 0, 0));
        removed.IsDeleted = true;

        FakeApiClient api = new();
        api.Pages.Add(new OrderPage([alive, removed], 2));

        OrderSyncService service = CreateService(api, orders, new InMemorySettingsStore(), new FakeSyncLogRepository());

        int marked = await service.ReconcileDeletedOrdersAsync();

        Assert.Equal(1, marked);
        Assert.Equal(["200"], orders.MarkedDeleted);

        OrderQuery query = Assert.Single(api.Queries);
        Assert.True(query.WithDeleted);
        Assert.Equal(["100", "200"], query.Numbers);

        // Явная нижняя граница по дате обязательна: без неё API ограничивает
        // выборку последними 30 днями даже при точном совпадении номера.
        Assert.NotNull(query.DateCreatedStart);
        Assert.True(query.DateCreatedStart < new DateTime(2026, 1, 10));
    }

    [Fact]
    public async Task Reconcile_keeps_orders_that_api_did_not_return()
    {
        FakeOrderRepository orders = new()
        {
            Active = [new ActiveOrderRef("300", new DateTime(2026, 5, 1))],
        };

        FakeApiClient api = new();
        api.Pages.Add(new OrderPage([], 0));

        OrderSyncService service = CreateService(api, orders, new InMemorySettingsStore(), new FakeSyncLogRepository());

        int marked = await service.ReconcileDeletedOrdersAsync();

        // Архивные заказы тоже не возвращаются по умолчанию, поэтому отсутствие
        // в ответе не считается удалением.
        Assert.Equal(0, marked);
        Assert.Empty(orders.MarkedDeleted);
    }

    [Fact]
    public async Task Reconcile_does_nothing_without_configuration_or_orders()
    {
        FakeApiClient api = new();

        OrderSyncService unconfigured = CreateService(
            api,
            new FakeOrderRepository { Active = [new ActiveOrderRef("1", null)] },
            new InMemorySettingsStore(),
            new FakeSyncLogRepository(),
            apiOptions: new AbcpApiOptions());

        Assert.Equal(0, await unconfigured.ReconcileDeletedOrdersAsync());

        OrderSyncService empty = CreateService(
            api,
            new FakeOrderRepository(),
            new InMemorySettingsStore(),
            new FakeSyncLogRepository());

        Assert.Equal(0, await empty.ReconcileDeletedOrdersAsync());
        Assert.Empty(api.Queries);
    }

    [Fact]
    public async Task Status_catalog_is_refreshed()
    {
        FakeApiClient api = new();
        api.Statuses.Add(new OrderStatusDto { Id = 1, Name = "Новый" });

        FakeStatusCatalogRepository statuses = new();

        OrderSyncService service = new(
            api,
            new FakeOrderRepository(),
            statuses,
            new FakeSyncLogRepository(),
            new InMemorySettingsStore(),
            new FakeSettingsProvider(new AbcpApiOptions(), new SyncOptions()),
            NullLogger<OrderSyncService>.Instance);

        int count = await service.RefreshStatusCatalogAsync();

        Assert.Equal(1, count);
        Assert.Single(statuses.Saved);
    }

    /// <summary>Клиент API, отдающий заготовленные страницы.</summary>
    private sealed class FakeApiClient : IAbcpApiClient
    {
        public List<OrderPage> Pages { get; } = [];

        public List<OrderQuery> Queries { get; } = [];

        public List<OrderStatusDto> Statuses { get; } = [];

        public Exception? Failure { get; set; }

        public Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Queries.Add(query);

            OrderPage page = Queries.Count <= Pages.Count
                ? Pages[Queries.Count - 1]
                : new OrderPage([], Pages.Count > 0 ? Pages[^1].TotalCount : 0);

            return Task.FromResult(page);
        }

        public Task<int> GetOrdersCountAsync(OrderQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Pages.Count > 0 ? Pages[0].TotalCount : 0);

        public Task<OrderDto?> GetOrderAsync(string number, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrderDto?>(null);

        public Task<IReadOnlyList<OrderStatusDto>> GetStatusesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderStatusDto>>(Statuses);

        public Task<IReadOnlyDictionary<long, IReadOnlyList<PositionStatusHistoryDto>>> GetStatusHistoryAsync(
            IReadOnlyCollection<long> positionIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<long, IReadOnlyList<PositionStatusHistoryDto>>>(
                new Dictionary<long, IReadOnlyList<PositionStatusHistoryDto>>());

        public Task<IReadOnlyList<ArticleInfoDto>> GetArticlesInfoAsync(
            IReadOnlyCollection<ArticleRef> articles,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArticleInfoDto>>([]);

        public Task<ConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionCheckResult(true, "ок"));
    }

    /// <summary>Репозиторий заказов в памяти.</summary>
    private sealed class FakeOrderRepository : IOrderRepository
    {
        public List<OrderDto> Upserted { get; } = [];

        public DateTime? MaxDateUpdated { get; set; }

        public Task<IReadOnlyList<OrderListItem>> GetListAsync(
            OrderFilter filter,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderListItem>>([]);

        public Task<int> CountAsync(OrderFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<Order?> GetByNumberAsync(string number, CancellationToken cancellationToken = default) =>
            Task.FromResult<Order?>(null);

        public Task<OrderChangeSet> UpsertAsync(
            IReadOnlyCollection<OrderDto> orders,
            CancellationToken cancellationToken = default)
        {
            Upserted.AddRange(orders);

            return Task.FromResult(new OrderChangeSet(
                orders.Select(order => order.Number).ToList(),
                [],
                []));
        }

        public Task<DateTime?> GetMaxDateUpdatedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(MaxDateUpdated);

        public List<ActiveOrderRef> Active { get; set; } = [];

        public List<string> MarkedDeleted { get; } = [];

        public Task<IReadOnlyList<ActiveOrderRef>> GetActiveOrderRefsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActiveOrderRef>>(Active);

        public Task<int> MarkDeletedAsync(
            IReadOnlyCollection<string> numbers,
            CancellationToken cancellationToken = default)
        {
            MarkedDeleted.AddRange(numbers);
            return Task.FromResult(numbers.Count);
        }
    }

    /// <summary>Справочник статусов в памяти.</summary>
    private sealed class FakeStatusCatalogRepository : IStatusCatalogRepository
    {
        public List<OrderStatusDto> Saved { get; } = [];

        public Task<IReadOnlyList<OrderStatus>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderStatus>>([]);

        public Task<int> UpsertAsync(
            IReadOnlyCollection<OrderStatusDto> statuses,
            CancellationToken cancellationToken = default)
        {
            Saved.AddRange(statuses);
            return Task.FromResult(Saved.Count);
        }
    }

    /// <summary>Журнал синхронизации в памяти.</summary>
    private sealed class FakeSyncLogRepository : ISyncLogRepository
    {
        public List<SyncLogEntry> Entries { get; } = [];

        public Task AddAsync(SyncLogEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SyncLogEntry>> GetRecentAsync(
            int take = 200,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SyncLogEntry>>(Entries);
    }

    /// <summary>Хранилище настроек в памяти.</summary>
    private sealed class InMemorySettingsStore : IAppSettingsStore
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out string? value) ? value : null);

        public Task SetAsync(
            string key,
            string? value,
            bool protect = false,
            CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string?>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string?>>(_values);

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.Remove(key));
    }

    /// <summary>Поставщик настроек с фиксированными значениями.</summary>
    private sealed class FakeSettingsProvider : IAbcpSettingsProvider
    {
        private readonly AbcpApiOptions _api;
        private readonly SyncOptions _sync;

        public FakeSettingsProvider(AbcpApiOptions api, SyncOptions sync)
        {
            _api = api;
            _sync = sync;
        }

        public Task<AbcpApiOptions> GetApiOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_api);

        public Task<SyncOptions> GetSyncOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_sync);

        public Task<CatalogOptions> GetCatalogOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogOptions());

        public Task<UpdateOptions> GetUpdateOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateOptions());

        public Task<PickingOptions> GetPickingOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PickingOptions());
    }
}
