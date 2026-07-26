using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Application.Services;
using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;
using ABCPClient.Infrastructure.Database;
using ABCPClient.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет задания на сборку: создание из заказов, признак наличия
/// и фиксацию собранного количества.
/// </summary>
/// <remarks>
/// Хранилище настоящее, на SQLite: правила опираются на уникальность номера
/// задания и на выборку незакрытых заданий, а это поведение базы.
/// </remarks>
public sealed class PickingServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-26T15:00:00+03:00", null);

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"abcpclient-picking-{Guid.NewGuid():N}.db");

    private TestFactory _factory = null!;
    private PickingRepository _picking = null!;
    private OrderRepository _orders = null!;
    private ArticleCardRepository _cards = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        DbContextOptions<AbcpDbContext> options = new DbContextOptionsBuilder<AbcpDbContext>()
            .UseSqlite(SqliteConnectionStringFactory.Create(_databasePath))
            .Options;

        _factory = new TestFactory(options);
        _picking = new PickingRepository(_factory);
        _cards = new ArticleCardRepository(_factory);
        _orders = new OrderRepository(_factory, NullLogger<OrderRepository>.Instance);

        await using AbcpDbContext context = _factory.CreateDbContext();
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Task_is_built_from_the_order()
    {
        await SaveOrderAsync("100", ("Febi", "01089", 2m, 56233), ("Bosch", "0258006537", 1m, 56233));

        PickingService service = Create();

        PickingTaskCreationResult result = await service.CreateTasksAsync(["100"]);

        PickingTaskListItem item = Assert.Single(result.Created);
        Assert.Equal("СБ-000001", item.Number);
        Assert.Equal("100", item.OrderNumber);
        Assert.Equal(2, item.LinesCount);

        PickingTask task = (await service.GetTaskAsync(item.Id))!;
        Assert.Equal(PickingTaskStatus.New, task.Status);

        PickingTaskLine line = task.Lines.Single(candidate => candidate.Number == "01089");
        Assert.Equal(2m, line.OrderedQuantity);
        Assert.Equal("febi|01089", line.MatchKey);
    }

    [Fact]
    public async Task Numbers_continue_and_stay_unique()
    {
        await SaveOrderAsync("100", ("Febi", "01089", 1m, null));
        await SaveOrderAsync("101", ("Febi", "01090", 1m, null));

        PickingService service = Create();

        await service.CreateTasksAsync(["100"]);
        PickingTaskCreationResult second = await service.CreateTasksAsync(["101"]);

        Assert.Equal("СБ-000002", Assert.Single(second.Created).Number);
    }

    [Fact]
    public async Task Second_task_for_the_same_order_is_refused()
    {
        await SaveOrderAsync("100", ("Febi", "01089", 1m, null));

        PickingService service = Create();

        await service.CreateTasksAsync(["100"]);
        PickingTaskCreationResult again = await service.CreateTasksAsync(["100"]);

        // Иначе товар собрали бы дважды.
        Assert.True(again.IsEmpty);
        Assert.Equal("100", Assert.Single(again.SkippedExisting));
    }

    [Fact]
    public async Task Closed_task_does_not_block_a_new_one()
    {
        await SaveOrderAsync("100", ("Febi", "01089", 1m, null));

        PickingService service = Create();

        PickingTaskListItem first = Assert.Single((await service.CreateTasksAsync(["100"])).Created);
        await service.CompleteTaskAsync(first.Id, "кладовщик");

        // Заказ могли дополнить — его собирают заново.
        PickingTaskCreationResult again = await service.CreateTasksAsync(["100"]);
        Assert.Single(again.Created);
    }

    [Fact]
    public async Task Unknown_order_and_empty_order_are_reported_separately()
    {
        await SaveOrderAsync("100", ("Febi", "01089", 1m, null), deleted: true);

        PickingService service = Create();

        PickingTaskCreationResult result = await service.CreateTasksAsync(["100", "999"]);

        Assert.True(result.IsEmpty);
        Assert.Equal("100", Assert.Single(result.SkippedEmpty));
        Assert.Equal("999", Assert.Single(result.NotFound));
    }

    [Fact]
    public async Task Empty_order_does_not_consume_a_number()
    {
        await SaveOrderAsync("100", ("Febi", "01089", 1m, null), deleted: true);
        await SaveOrderAsync("101", ("Bosch", "0258006537", 1m, null));

        PickingService service = Create();

        PickingTaskCreationResult result = await service.CreateTasksAsync(["100", "101"]);

        Assert.Equal("СБ-000001", Assert.Single(result.Created).Number);
    }

    [Fact]
    public async Task Availability_comes_from_the_configured_statuses()
    {
        await SaveOrderAsync(
            "100",
            ("Febi", "01089", 1m, 111),
            ("Bosch", "0258006537", 1m, 222),
            ("Sachs", "3182654213", 1m, 333));

        PickingService service = Create(new PickingOptions
        {
            InStockStatusCodes = [111],
            IncomingStatusCodes = [222],
            TreatDeadlineAsIncoming = false,
        });

        PickingTaskListItem item = Assert.Single((await service.CreateTasksAsync(["100"])).Created);
        PickingTask task = (await service.GetTaskAsync(item.Id))!;

        Assert.Equal(StockAvailability.InStock, Line(task, "01089").Availability);
        Assert.Equal(StockAvailability.Incoming, Line(task, "0258006537").Availability);

        // Неизвестный статус — честное «нет данных», а не «нет в наличии».
        Assert.Equal(StockAvailability.Unknown, Line(task, "3182654213").Availability);

        Assert.Equal(1, item.InStockLines);
        Assert.Equal(1, item.IncomingLines);
    }

    [Fact]
    public async Task Only_goods_in_stock_are_counted_as_available_quantity()
    {
        await SaveOrderAsync("100", ("Febi", "01089", 3m, 111), ("Bosch", "0258006537", 5m, 222));

        PickingService service = Create(new PickingOptions
        {
            InStockStatusCodes = [111],
            IncomingStatusCodes = [222],
        });

        PickingTask task = (await service.GetTaskAsync(
            Assert.Single((await service.CreateTasksAsync(["100"])).Created).Id))!;

        Assert.Equal(3m, Line(task, "01089").AvailableQuantity);
        Assert.Equal(0m, Line(task, "0258006537").AvailableQuantity);
    }

    [Fact]
    public async Task Deadline_marks_the_line_as_incoming_with_an_eta()
    {
        await SaveOrderAsync("100", ("Febi", "01089", 1m, 999, DeadlineHours: 48));

        PickingService service = Create();

        PickingTask task = (await service.GetTaskAsync(
            Assert.Single((await service.CreateTasksAsync(["100"])).Created).Id))!;

        PickingTaskLine line = Line(task, "01089");
        Assert.Equal(StockAvailability.Incoming, line.Availability);
        Assert.Equal(Now.LocalDateTime.AddHours(48), line.IncomingEta);
    }

    [Fact]
    public async Task Barcodes_and_names_are_copied_from_the_card_cache()
    {
        await SaveOrderAsync("100", ("Febi", "01089", 1m, null));

        await _cards.UpsertAsync(
        [
            new ArticleCard
            {
                Brand = "Febi",
                Number = "01089",
                Description = "Опора двигателя",
                Barcodes = "4640562802795",
                Source = ArticleCardSource.Catalog,
                SyncedAt = Now.LocalDateTime,
            },
        ]);

        PickingService service = Create();

        PickingTask task = (await service.GetTaskAsync(
            Assert.Single((await service.CreateTasksAsync(["100"])).Created).Id))!;

        PickingTaskLine line = Line(task, "01089");

        // Снимок, а не ссылка: терминал ищет по сканеру и без сети.
        Assert.Equal("4640562802795", line.Barcodes);
        Assert.Equal("Опора двигателя", line.Description);
    }

    [Fact]
    public async Task Repeated_pick_does_not_double_the_fact()
    {
        PickingService service = Create(new PickingOptions { InStockStatusCodes = [111] });
        await SaveOrderAsync("100", ("Febi", "01089", 5m, 111));

        PickingTask task = (await service.GetTaskAsync(
            Assert.Single((await service.CreateTasksAsync(["100"])).Created).Id))!;

        int lineId = task.Lines[0].Id;

        await service.RegisterPickAsync(new PickRequest(task.Id, lineId, 2m, "ТСД-1"));
        PickingTask after = await service.RegisterPickAsync(new PickRequest(task.Id, lineId, 2m, "ТСД-1"));

        // Терминал повторяет отправку при обрыве связи: значение задаётся, а не растёт.
        Assert.Equal(2m, after.Lines[0].PickedQuantity);
        Assert.Equal(PickingTaskStatus.InProgress, after.Status);
        Assert.NotNull(after.StartedAt);
    }

    [Fact]
    public async Task Picking_more_than_ordered_is_capped()
    {
        PickingService service = Create(new PickingOptions { InStockStatusCodes = [111] });
        await SaveOrderAsync("100", ("Febi", "01089", 2m, 111));

        PickingTask task = (await service.GetTaskAsync(
            Assert.Single((await service.CreateTasksAsync(["100"])).Created).Id))!;

        PickingTask after = await service.RegisterPickAsync(
            new PickRequest(task.Id, task.Lines[0].Id, 100m, "ТСД-1"));

        Assert.Equal(2m, after.Lines[0].PickedQuantity);
        Assert.Equal(PickingTaskStatus.Picked, after.Status);
    }

    [Fact]
    public async Task Task_becomes_picked_when_everything_in_stock_is_collected()
    {
        PickingService service = Create(new PickingOptions
        {
            InStockStatusCodes = [111],
            IncomingStatusCodes = [222],
        });

        await SaveOrderAsync("100", ("Febi", "01089", 1m, 111), ("Bosch", "0258006537", 1m, 222));

        PickingTask task = (await service.GetTaskAsync(
            Assert.Single((await service.CreateTasksAsync(["100"])).Created).Id))!;

        PickingTaskLine inStock = Line(task, "01089");

        PickingTask after = await service.RegisterPickAsync(
            new PickRequest(task.Id, inStock.Id, 1m, "ТСД-1"));

        // Строку в пути собрать нельзя, и ждать её означало бы никогда не закрыть задание.
        Assert.Equal(PickingTaskStatus.Picked, after.Status);
    }

    [Fact]
    public async Task Pick_for_a_foreign_line_is_refused()
    {
        PickingService service = Create();
        await SaveOrderAsync("100", ("Febi", "01089", 1m, null));

        PickingTask task = (await service.GetTaskAsync(
            Assert.Single((await service.CreateTasksAsync(["100"])).Created).Id))!;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RegisterPickAsync(new PickRequest(task.Id, 99999, 1m, "ТСД-1")));
    }

    [Fact]
    public async Task Cancelled_task_accepts_no_picks()
    {
        PickingService service = Create();
        await SaveOrderAsync("100", ("Febi", "01089", 1m, null));

        PickingTask task = (await service.GetTaskAsync(
            Assert.Single((await service.CreateTasksAsync(["100"])).Created).Id))!;

        await service.CancelTaskAsync(task.Id, "заказ отменён клиентом");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RegisterPickAsync(new PickRequest(task.Id, task.Lines[0].Id, 1m, "ТСД-1")));
    }

    [Fact]
    public async Task Closed_task_cannot_be_cancelled()
    {
        PickingService service = Create();
        await SaveOrderAsync("100", ("Febi", "01089", 1m, null));

        PickingTaskListItem item = Assert.Single((await service.CreateTasksAsync(["100"])).Created);
        await service.CompleteTaskAsync(item.Id, "кладовщик");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelTaskAsync(item.Id, "поздно"));
    }

    [Fact]
    public async Task Open_filter_hides_closed_and_cancelled_tasks()
    {
        PickingService service = Create();

        await SaveOrderAsync("100", ("Febi", "01089", 1m, null));
        await SaveOrderAsync("101", ("Febi", "01090", 1m, null));
        await SaveOrderAsync("102", ("Febi", "01091", 1m, null));

        PickingTaskCreationResult created = await service.CreateTasksAsync(["100", "101", "102"]);

        await service.CompleteTaskAsync(created.Created[0].Id, "кладовщик");
        await service.CancelTaskAsync(created.Created[1].Id, "отмена");

        IReadOnlyList<PickingTaskListItem> open = await service.GetTasksAsync(
            new PickingTaskFilter { OnlyOpen = true });

        Assert.Equal("102", Assert.Single(open).OrderNumber);
        Assert.Equal(3, (await service.GetTasksAsync(new PickingTaskFilter())).Count);
    }

    [Fact]
    public async Task Tasks_are_searchable_by_order_and_customer()
    {
        PickingService service = Create();
        await SaveOrderAsync("75892367", ("Febi", "01089", 1m, null));

        await service.CreateTasksAsync(["75892367"]);

        Assert.Single(await service.GetTasksAsync(new PickingTaskFilter { SearchText = "7589" }));
        Assert.Single(await service.GetTasksAsync(new PickingTaskFilter { SearchText = "СБ-0000" }));
        Assert.Single(await service.GetTasksAsync(new PickingTaskFilter { SearchText = "Дойч" }));
        Assert.Empty(await service.GetTasksAsync(new PickingTaskFilter { SearchText = "нетакого" }));
    }

    private static PickingTaskLine Line(PickingTask task, string number) =>
        task.Lines.Single(line => line.Number == number);

    private PickingService Create(PickingOptions? options = null) =>
        new(
            _picking,
            _orders,
            _cards,
            new PickingSettings(options ?? new PickingOptions()),
            NullLogger<PickingService>.Instance)
        {
            Time = new FixedTime(Now),
        };

    private async Task SaveOrderAsync(
        string number,
        params (string Brand, string Article, decimal Quantity, int? StatusCode)[] items) =>
        await SaveOrderAsync(number, false, items.Select(item =>
            (item.Brand, item.Article, item.Quantity, item.StatusCode, (int?)null)).ToArray());

    private async Task SaveOrderAsync(
        string number,
        (string Brand, string Article, decimal Quantity, int? StatusCode, int? DeadlineHours) item) =>
        await SaveOrderAsync(number, false, [item]);

    private async Task SaveOrderAsync(
        string number,
        (string Brand, string Article, decimal Quantity, int? StatusCode) item,
        bool deleted) =>
        await SaveOrderAsync(
            number,
            deleted,
            [(item.Brand, item.Article, item.Quantity, item.StatusCode, (int?)null)]);

    private async Task SaveOrderAsync(
        string number,
        bool deleted,
        (string Brand, string Article, decimal Quantity, int? StatusCode, int? DeadlineHours)[] items)
    {
        await using AbcpDbContext context = _factory.CreateDbContext();

        Order order = new()
        {
            Number = number,
            InternalNumber = "УТ-" + number,
            UserName = "Клиент",
            UserFullName = "ООО «Дойч-Авто ДА»",
            Sum = 1000m,
            PositionsQuantity = items.Length,
            Date = Now.LocalDateTime,
            SyncedAt = Now.LocalDateTime,
        };

        long position = context.OrderItems.Any() ? context.OrderItems.Max(candidate => candidate.PositionId) : 0;

        foreach ((string brand, string article, decimal quantity, int? status, int? deadline) in items)
        {
            order.Items.Add(new OrderItem
            {
                PositionId = ++position,
                Brand = brand,
                Number = article,
                Quantity = quantity,
                QuantityFinal = quantity,
                PriceOut = 500m,
                StatusCode = status,
                DeadlineHours = deadline,
                IsDeleted = deleted,
            });
        }

        order.RefreshStatusAggregate();

        context.Orders.Add(order);
        await context.SaveChangesAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        foreach (string path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    private sealed class TestFactory : IDbContextFactory<AbcpDbContext>
    {
        private readonly DbContextOptions<AbcpDbContext> _options;

        public TestFactory(DbContextOptions<AbcpDbContext> options) => _options = options;

        public AbcpDbContext CreateDbContext() => new(_options);
    }

    /// <summary>
    /// Источник времени с постоянным значением.
    /// </summary>
    /// <remarks>
    /// Значение приводится к UTC: по контракту <see cref="TimeProvider.GetUtcNow"/>
    /// обязан возвращать время в UTC, а <see cref="TimeProvider.GetLocalNow"/>
    /// строится из него прибавлением местного смещения. Вернув отсюда время
    /// со смещением, мы получили бы смещение, учтённое дважды.
    /// </remarks>
    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTime(DateTimeOffset now) => _utcNow = now.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class PickingSettings : IAbcpSettingsProvider
    {
        private readonly PickingOptions _picking;

        public PickingSettings(PickingOptions picking) => _picking = picking;

        public Task<AbcpApiOptions> GetApiOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AbcpApiOptions());

        public Task<SyncOptions> GetSyncOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncOptions());

        public Task<CatalogOptions> GetCatalogOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogOptions());

        public Task<UpdateOptions> GetUpdateOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateOptions());

        public Task<PickingOptions> GetPickingOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_picking);
    }
}
