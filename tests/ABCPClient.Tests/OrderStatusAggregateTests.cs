using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет правило сведения статусов позиций к статусу заказа.
/// </summary>
public sealed class OrderStatusAggregateTests
{
    private static OrderItem Item(int? statusCode, string? status = null, bool deleted = false) => new()
    {
        StatusCode = statusCode,
        Status = status,
        IsDeleted = deleted,
    };

    [Fact]
    public void Single_status_is_not_mixed()
    {
        OrderStatusAggregate aggregate = OrderStatusAggregate.FromItems(
        [
            Item(56233, "В работе"),
            Item(56233, "В работе"),
        ]);

        Assert.Equal(56233, aggregate.DominantStatusCode);
        Assert.Equal("В работе", aggregate.DominantStatusName);
        Assert.False(aggregate.IsMixed);
        Assert.Equal(2, aggregate.CountedItems);
        Assert.Equal("В работе", aggregate.DisplayText);
    }

    [Fact]
    public void Most_frequent_status_wins_and_order_is_marked_mixed()
    {
        OrderStatusAggregate aggregate = OrderStatusAggregate.FromItems(
        [
            Item(10, "Новый"),
            Item(20, "Заказан"),
            Item(20, "Заказан"),
        ]);

        Assert.Equal(20, aggregate.DominantStatusCode);
        Assert.True(aggregate.IsMixed);
        Assert.Equal(2, aggregate.DistinctStatusCount);
        Assert.Equal("Заказан (+1)", aggregate.DisplayText);
    }

    [Fact]
    public void Ties_are_resolved_by_smaller_status_code()
    {
        OrderStatusAggregate aggregate = OrderStatusAggregate.FromItems(
        [
            Item(30, "Выдан"),
            Item(10, "Новый"),
        ]);

        Assert.Equal(10, aggregate.DominantStatusCode);
    }

    [Fact]
    public void Deleted_and_statusless_items_are_ignored()
    {
        OrderStatusAggregate aggregate = OrderStatusAggregate.FromItems(
        [
            Item(10, "Новый", deleted: true),
            Item(null),
            Item(40, "Отгружен"),
        ]);

        Assert.Equal(40, aggregate.DominantStatusCode);
        Assert.Equal(1, aggregate.CountedItems);
        Assert.False(aggregate.IsMixed);
    }

    [Fact]
    public void Empty_order_has_no_status()
    {
        OrderStatusAggregate aggregate = OrderStatusAggregate.FromItems([]);

        Assert.Null(aggregate.DominantStatusCode);
        Assert.Equal("Без статуса", aggregate.DisplayText);
        Assert.False(aggregate.IsMixed);
    }

    [Fact]
    public void Refresh_updates_order_projection()
    {
        Order order = new();
        order.Items.Add(Item(10, "Новый"));
        order.Items.Add(Item(20, "Заказан"));
        order.Items.Add(Item(20, "Заказан"));

        order.RefreshStatusAggregate();

        Assert.Equal(20, order.DominantStatusCode);
        Assert.Equal("Заказан", order.DominantStatusName);
        Assert.True(order.HasMixedStatuses);
    }
}
