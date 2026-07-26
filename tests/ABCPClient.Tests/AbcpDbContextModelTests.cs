using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;
using ABCPClient.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет модель базы данных: таблицы, ключи и сохранение графа заказа.
/// </summary>
public sealed class AbcpDbContextModelTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"abcpclient-model-{Guid.NewGuid():N}.db");

    private AbcpDbContext CreateContext()
    {
        DbContextOptions<AbcpDbContext> options = new DbContextOptionsBuilder<AbcpDbContext>()
            .UseSqlite(SqliteConnectionStringFactory.Create(_databasePath))
            .Options;

        return new AbcpDbContext(options);
    }

    [Fact]
    public void Model_maps_expected_tables()
    {
        using AbcpDbContext context = CreateContext();

        string[] tables = context.Model
            .GetEntityTypes()
            .Select(entity => entity.GetTableName()!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "ArticleCards",
                "OrderItemStatusHistory",
                "OrderItems",
                "OrderStatuses",
                "Orders",
                "Settings",
                "SyncLog",
            ],
            tables);
    }

    [Fact]
    public void Order_number_and_position_id_are_unique()
    {
        using AbcpDbContext context = CreateContext();

        Assert.Contains(
            context.Model.FindEntityType(typeof(Order))!.GetIndexes(),
            index => index.IsUnique && index.Properties.Single().Name == nameof(Order.Number));

        Assert.Contains(
            context.Model.FindEntityType(typeof(OrderItem))!.GetIndexes(),
            index => index.IsUnique && index.Properties.Single().Name == nameof(OrderItem.PositionId));
    }

    [Fact]
    public async Task Order_graph_round_trips_through_database()
    {
        await using (AbcpDbContext context = CreateContext())
        {
            await context.Database.EnsureCreatedAsync(CancellationToken.None);

            Order order = new()
            {
                Number = "75892367",
                InternalNumber = "УТ-000123",
                Sum = 1543.50m,
                PositionsQuantity = 2,
                Date = new DateTime(2026, 7, 24, 12, 31, 5),
                DateUpdated = new DateTime(2026, 7, 25, 9, 14, 0),
                SyncedAt = new DateTime(2026, 7, 25, 9, 20, 0),
            };

            order.Items.Add(new OrderItem
            {
                PositionId = 469961941,
                Brand = "Febi",
                Number = "01089",
                Quantity = 2m,
                QuantityFinal = 2m,
                PriceOut = 771.75m,
                StatusCode = 56233,
                Status = "В работе",
                DistributorType = DistributorType.Online,
                CancelRequest = CancelRequestState.NotRequested,
                CurrencyOutId = CurrencyId.RussianRuble,
                StatusHistory =
                [
                    new OrderItemStatusHistoryEntry
                    {
                        StatusCode = 56233,
                        Status = "В работе",
                        ChangedAt = new DateTime(2026, 7, 25, 9, 14, 0),
                        ManagerName = "Иванов",
                    },
                ],
            });

            order.RefreshStatusAggregate();

            context.Orders.Add(order);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (AbcpDbContext context = CreateContext())
        {
            Order loaded = await context.Orders
                .Include(o => o.Items)
                .ThenInclude(i => i.StatusHistory)
                .SingleAsync(o => o.Number == "75892367", CancellationToken.None);

            Assert.Equal("УТ-000123", loaded.InternalNumber);
            Assert.Equal(1543.50m, loaded.Sum);
            Assert.Equal(56233, loaded.DominantStatusCode);
            Assert.False(loaded.HasMixedStatuses);

            OrderItem item = Assert.Single(loaded.Items);
            Assert.Equal(469961941, item.PositionId);
            Assert.Equal(DistributorType.Online, item.DistributorType);
            Assert.Equal(CurrencyId.RussianRuble, item.CurrencyOutId);
            Assert.Equal(1543.50m, loaded.Sum);
            Assert.Equal(2m * 771.75m, item.Total);

            OrderItemStatusHistoryEntry history = Assert.Single(item.StatusHistory);
            Assert.Equal("Иванов", history.ManagerName);
        }
    }

    [Fact]
    public async Task Deleting_order_cascades_to_items_and_history()
    {
        await using AbcpDbContext context = CreateContext();
        await context.Database.EnsureCreatedAsync(CancellationToken.None);

        Order order = new() { Number = "1000", SyncedAt = DateTime.Now };
        order.Items.Add(new OrderItem
        {
            PositionId = 1,
            Brand = "B",
            Number = "N",
            StatusHistory = [new OrderItemStatusHistoryEntry { StatusCode = 1, ChangedAt = DateTime.Now }],
        });

        context.Orders.Add(order);
        await context.SaveChangesAsync(CancellationToken.None);

        context.Orders.Remove(order);
        await context.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(await context.OrderItems.ToListAsync(CancellationToken.None));
        Assert.Empty(await context.OrderItemStatusHistory.ToListAsync(CancellationToken.None));
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
