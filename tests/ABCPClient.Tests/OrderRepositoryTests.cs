using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Entities;
using ABCPClient.Infrastructure.Database;
using ABCPClient.Infrastructure.Repositories;
using ABCPClient.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет репозиторий заказов на реальной базе SQLite: применение данных API,
/// обнаружение изменений, фильтры и справочник статусов.
/// </summary>
public sealed class OrderRepositoryTests : IAsyncLifetime, IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"abcpclient-repo-{Guid.NewGuid():N}.db");

    private IDbContextFactory<AbcpDbContext> _contextFactory = null!;
    private OrderRepository _repository = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        DbContextOptions<AbcpDbContext> options = new DbContextOptionsBuilder<AbcpDbContext>()
            .UseSqlite(SqliteConnectionStringFactory.Create(_databasePath))
            .Options;

        _contextFactory = new Factory(options);

        await using AbcpDbContext context = _contextFactory.CreateDbContext();
        await context.Database.MigrateAsync(CancellationToken.None);

        _repository = new OrderRepository(_contextFactory, NullLogger<OrderRepository>.Instance);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    private static OrderDto Order(
        string number,
        DateTime updated,
        params OrderPositionDto[] positions) => new()
    {
        Number = number,
        InternalNumber = $"УТ-{number}",
        UserName = "Иванов",
        UserFullName = "ООО Ромашка",
        Sum = positions.Sum(position => position.PriceOut * position.QuantityFinal),
        PositionsQuantity = positions.Length,
        Date = new DateTime(2026, 7, 20, 10, 0, 0),
        DateUpdated = updated,
        Positions = positions.ToList(),
    };

    private static OrderPositionDto Position(long id, int? statusCode, string status, string brand = "Febi") => new()
    {
        Id = id,
        Brand = brand,
        Number = "01089",
        Quantity = 1m,
        QuantityFinal = 1m,
        PriceOut = 500m,
        StatusCode = statusCode,
        Status = status,
        StatusChangeDate = new DateTime(2026, 7, 25, 9, 0, 0),
    };

    [Fact]
    public async Task New_order_is_created_with_positions_and_aggregate()
    {
        OrderChangeSet changes = await _repository.UpsertAsync(
        [
            Order("100", new DateTime(2026, 7, 25, 9, 0, 0), Position(1, 10, "Новый"), Position(2, 10, "Новый")),
        ]);

        Assert.Equal(["100"], changes.CreatedOrders);
        Assert.Empty(changes.UpdatedOrders);

        // Смены статусов у нового заказа быть не может: сравнивать не с чем.
        Assert.Empty(changes.StatusChanges);

        Order? stored = await _repository.GetByNumberAsync("100");
        Assert.NotNull(stored);
        Assert.Equal(2, stored.Items.Count);
        Assert.Equal(10, stored.DominantStatusCode);
        Assert.False(stored.HasMixedStatuses);
    }

    [Fact]
    public async Task Repeated_order_without_changes_is_not_reported_as_updated()
    {
        OrderDto dto = Order("200", new DateTime(2026, 7, 25, 9, 0, 0), Position(21, 10, "Новый"));

        await _repository.UpsertAsync([dto]);
        OrderChangeSet second = await _repository.UpsertAsync([dto]);

        Assert.Empty(second.CreatedOrders);
        Assert.Empty(second.UpdatedOrders);
        Assert.Empty(second.StatusChanges);
    }

    [Fact]
    public async Task Status_change_is_detected_and_written_to_history()
    {
        await _repository.UpsertAsync(
            [Order("300", new DateTime(2026, 7, 25, 9, 0, 0), Position(31, 10, "Новый"))]);

        OrderChangeSet changes = await _repository.UpsertAsync(
            [Order("300", new DateTime(2026, 7, 25, 11, 0, 0), Position(31, 20, "Заказан"))]);

        Assert.Equal(["300"], changes.UpdatedOrders);

        OrderStatusChange change = Assert.Single(changes.StatusChanges);
        Assert.Equal("300", change.OrderNumber);
        Assert.Equal(31, change.PositionId);
        Assert.Equal(10, change.PreviousStatusCode);
        Assert.Equal("Новый", change.PreviousStatus);
        Assert.Equal(20, change.CurrentStatusCode);
        Assert.Equal("Заказан", change.CurrentStatus);

        await using AbcpDbContext context = _contextFactory.CreateDbContext();
        Assert.Single(await context.OrderItemStatusHistory.ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Mixed_statuses_are_flagged()
    {
        await _repository.UpsertAsync(
        [
            Order("400", new DateTime(2026, 7, 25, 9, 0, 0), Position(41, 10, "Новый"), Position(42, 20, "Заказан")),
        ]);

        Order? stored = await _repository.GetByNumberAsync("400");

        Assert.NotNull(stored);
        Assert.True(stored.HasMixedStatuses);

        IReadOnlyList<OrderListItem> list = await _repository.GetListAsync(new OrderFilter());
        OrderListItem row = Assert.Single(list, item => item.Number == "400");
        Assert.Contains("смешанный", row.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_positions_are_appended_to_existing_order()
    {
        await _repository.UpsertAsync(
            [Order("500", new DateTime(2026, 7, 25, 9, 0, 0), Position(51, 10, "Новый"))]);

        await _repository.UpsertAsync(
        [
            Order("500", new DateTime(2026, 7, 25, 10, 0, 0), Position(51, 10, "Новый"), Position(52, 10, "Новый")),
        ]);

        Order? stored = await _repository.GetByNumberAsync("500");
        Assert.NotNull(stored);
        Assert.Equal(2, stored.Items.Count);
    }

    [Fact]
    public async Task Short_format_response_keeps_existing_positions()
    {
        await _repository.UpsertAsync(
            [Order("600", new DateTime(2026, 7, 25, 9, 0, 0), Position(61, 10, "Новый"))]);

        // format=short не отдаёт позиции — состав заказа не должен обнулиться.
        await _repository.UpsertAsync([Order("600", new DateTime(2026, 7, 25, 12, 0, 0))]);

        Order? stored = await _repository.GetByNumberAsync("600");
        Assert.NotNull(stored);
        Assert.Single(stored.Items);
    }

    [Fact]
    public async Task Search_matches_order_customer_and_position()
    {
        await _repository.UpsertAsync(
        [
            Order("700", new DateTime(2026, 7, 25, 9, 0, 0), Position(71, 10, "Новый", brand: "Bosch")),
            Order("701", new DateTime(2026, 7, 25, 9, 0, 0), Position(72, 10, "Новый", brand: "Febi")),
        ]);

        Assert.Single(await _repository.GetListAsync(new OrderFilter { SearchText = "700" }));
        Assert.Single(await _repository.GetListAsync(new OrderFilter { SearchText = "УТ-701" }));
        Assert.Single(await _repository.GetListAsync(new OrderFilter { SearchText = "Bosch" }));
        Assert.Equal(2, (await _repository.GetListAsync(new OrderFilter { SearchText = "Ромашка" })).Count);
        Assert.Empty(await _repository.GetListAsync(new OrderFilter { SearchText = "Volvo" }));
    }

    [Fact]
    public async Task Status_and_date_filters_are_applied()
    {
        await _repository.UpsertAsync(
        [
            Order("800", new DateTime(2026, 7, 25, 9, 0, 0), Position(81, 10, "Новый")),
            Order("801", new DateTime(2026, 7, 25, 9, 0, 0), Position(82, 20, "Заказан")),
        ]);

        Assert.Single(await _repository.GetListAsync(new OrderFilter { StatusCode = 20 }));
        Assert.Equal(2, await _repository.CountAsync(new OrderFilter()));

        Assert.Equal(
            2,
            await _repository.CountAsync(new OrderFilter { DateFrom = new DateTime(2026, 7, 20) }));

        // Верхняя граница включает весь указанный день.
        Assert.Equal(
            2,
            await _repository.CountAsync(new OrderFilter { DateTo = new DateTime(2026, 7, 20) }));

        Assert.Equal(
            0,
            await _repository.CountAsync(new OrderFilter { DateFrom = new DateTime(2026, 7, 21) }));
    }

    [Fact]
    public async Task Deleted_orders_are_hidden_unless_requested()
    {
        OrderDto deleted = Order("900", new DateTime(2026, 7, 25, 9, 0, 0), Position(91, 10, "Новый"));
        deleted.IsDeleted = true;

        await _repository.UpsertAsync([deleted]);

        Assert.Empty(await _repository.GetListAsync(new OrderFilter()));
        Assert.Single(await _repository.GetListAsync(new OrderFilter { IncludeDeleted = true }));
    }

    [Fact]
    public async Task Order_deleted_in_portal_disappears_from_list()
    {
        // Заказ пришёл живым…
        await _repository.UpsertAsync(
            [Order("1200", new DateTime(2026, 7, 25, 9, 0, 0), Position(121, 10, "Новый"))]);

        Assert.Single(await _repository.GetListAsync(new OrderFilter()));

        // …а затем API (с withDeleted=1) отдал его с признаком удаления.
        OrderDto deleted = Order("1200", new DateTime(2026, 7, 25, 16, 0, 0), Position(121, 10, "Новый"));
        deleted.IsDeleted = true;

        OrderChangeSet changes = await _repository.UpsertAsync([deleted]);

        Assert.Equal(["1200"], changes.UpdatedOrders);
        Assert.Empty(await _repository.GetListAsync(new OrderFilter()));

        // Позиции удалённого заказа тоже неактуальны — фильтр по статусу его не находит.
        Assert.Empty(await _repository.GetListAsync(new OrderFilter { StatusCode = 10 }));
    }

    [Fact]
    public async Task Mark_deleted_hides_order_and_its_positions()
    {
        await _repository.UpsertAsync(
        [
            Order("1300", new DateTime(2026, 7, 25, 9, 0, 0), Position(131, 10, "Новый")),
            Order("1301", new DateTime(2026, 7, 25, 9, 0, 0), Position(132, 10, "Новый")),
        ]);

        int marked = await _repository.MarkDeletedAsync(["1300"]);

        Assert.Equal(1, marked);

        OrderListItem row = Assert.Single(await _repository.GetListAsync(new OrderFilter()));
        Assert.Equal("1301", row.Number);

        IReadOnlyList<OrderListItem> byStatus = await _repository.GetListAsync(new OrderFilter { StatusCode = 10 });
        Assert.DoesNotContain("1300", byStatus.Select(item => item.Number));

        // Повторная пометка уже удалённого заказа ничего не меняет.
        Assert.Equal(0, await _repository.MarkDeletedAsync(["1300"]));

        // Неизвестные номера не приводят к ошибке.
        Assert.Equal(0, await _repository.MarkDeletedAsync(["нет-такого"]));
    }

    [Fact]
    public async Task List_marks_deleted_orders_when_they_are_shown()
    {
        await _repository.UpsertAsync(
        [
            Order("1500", new DateTime(2026, 7, 25, 9, 0, 0), Position(151, 10, "Новый")),
            Order("1501", new DateTime(2026, 7, 25, 9, 0, 0), Position(152, 10, "Новый")),
        ]);

        await _repository.MarkDeletedAsync(["1500"]);

        IReadOnlyList<OrderListItem> withDeleted =
            await _repository.GetListAsync(new OrderFilter { IncludeDeleted = true });

        Assert.Equal(2, withDeleted.Count);
        Assert.True(Assert.Single(withDeleted, item => item.Number == "1500").IsDeleted);
        Assert.False(Assert.Single(withDeleted, item => item.Number == "1501").IsDeleted);
    }

    [Fact]
    public async Task Active_snapshot_excludes_deleted_orders()
    {
        await _repository.UpsertAsync(
        [
            Order("1400", new DateTime(2026, 7, 25, 9, 0, 0), Position(141, 10, "Новый")),
            Order("1401", new DateTime(2026, 7, 25, 9, 0, 0), Position(142, 10, "Новый")),
        ]);

        await _repository.MarkDeletedAsync(["1400"]);

        IReadOnlyList<ActiveOrderRef> snapshot = await _repository.GetActiveOrderRefsAsync();

        Assert.Equal(["1401"], snapshot.Select(item => item.Number));
        Assert.Equal(new DateTime(2026, 7, 20, 10, 0, 0), Assert.Single(snapshot).Date);
    }

    [Fact]
    public async Task Max_date_updated_is_returned_for_watermark()
    {
        await _repository.UpsertAsync(
        [
            Order("1000", new DateTime(2026, 7, 25, 9, 0, 0), Position(101, 10, "Новый")),
            Order("1001", new DateTime(2026, 7, 25, 15, 45, 0), Position(102, 10, "Новый")),
        ]);

        Assert.Equal(new DateTime(2026, 7, 25, 15, 45, 0), await _repository.GetMaxDateUpdatedAsync());
    }

    [Fact]
    public async Task Status_color_is_taken_from_catalog()
    {
        StatusCatalogRepository statuses = new(_contextFactory);
        await statuses.UpsertAsync([new OrderStatusDto { Id = 10, Name = "Новый", Color = "#00aa00" }]);

        await _repository.UpsertAsync(
            [Order("1100", new DateTime(2026, 7, 25, 9, 0, 0), Position(111, 10, "Новый"))]);

        OrderListItem row = Assert.Single(await _repository.GetListAsync(new OrderFilter()));
        Assert.Equal("#00aa00", row.StatusColor);
    }

    [Fact]
    public async Task Status_catalog_upsert_updates_existing_entries()
    {
        StatusCatalogRepository statuses = new(_contextFactory);

        await statuses.UpsertAsync([new OrderStatusDto { Id = 5, Name = "Старое имя", Color = "#111111" }]);
        int count = await statuses.UpsertAsync(
            [new OrderStatusDto { Id = 5, Name = "Новое имя", Color = "#222222", Notify = true }]);

        Assert.Equal(1, count);

        OrderStatus stored = Assert.Single(await statuses.GetAllAsync());
        Assert.Equal("Новое имя", stored.Name);
        Assert.Equal("#222222", stored.Color);
        Assert.True(stored.Notify);
    }

    [Fact]
    public async Task Sync_log_returns_recent_entries_first()
    {
        SyncLogRepository log = new(_contextFactory);

        await log.AddAsync(new SyncLogEntry
        {
            Operation = Domain.Models.SyncOperation.Orders,
            Outcome = Domain.Models.SyncOutcome.Success,
            StartedAt = new DateTime(2026, 7, 25, 9, 0, 0),
            FinishedAt = new DateTime(2026, 7, 25, 9, 0, 5),
        });

        await log.AddAsync(new SyncLogEntry
        {
            Operation = Domain.Models.SyncOperation.Orders,
            Outcome = Domain.Models.SyncOutcome.Failed,
            StartedAt = new DateTime(2026, 7, 25, 10, 0, 0),
            ErrorCode = 102,
        });

        IReadOnlyList<SyncLogEntry> entries = await log.GetRecentAsync(10);

        Assert.Equal(2, entries.Count);
        Assert.Equal(Domain.Models.SyncOutcome.Failed, entries[0].Outcome);
        Assert.Equal(TimeSpan.FromSeconds(5), entries[1].Duration);
    }

    /// <summary>Фабрика контекстов для тестов.</summary>
    private sealed class Factory : IDbContextFactory<AbcpDbContext>
    {
        private readonly DbContextOptions<AbcpDbContext> _options;

        public Factory(DbContextOptions<AbcpDbContext> options) => _options = options;

        public AbcpDbContext CreateDbContext() => new(_options);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (string path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

/// <summary>
/// Проверяет преобразование пароля в параметр <c>userpsw</c>.
/// </summary>
public sealed class Md5PasswordHasherTests
{
    private readonly Md5PasswordHasher _hasher = new();

    [Fact]
    public void Known_password_produces_known_md5()
    {
        // Контрольное значение md5("password") — формат задан протоколом API.
        Assert.Equal("5f4dcc3b5aa765d61d8327deb882cf99", _hasher.ToApiHash("password"));
    }

    [Fact]
    public void Hash_is_lowercase_hex_of_32_chars()
    {
        string hash = _hasher.ToApiHash("Пароль-123");

        Assert.Equal(32, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.True(_hasher.LooksLikeHash(hash));
    }

    [Fact]
    public void Ready_hash_is_recognised_and_other_strings_are_not()
    {
        Assert.True(_hasher.LooksLikeHash("0123456789abcdef0123456789ABCDEF"));
        Assert.False(_hasher.LooksLikeHash("короткая строка"));
        Assert.False(_hasher.LooksLikeHash(null));
        Assert.False(_hasher.LooksLikeHash(new string('z', 32)));
    }
}
