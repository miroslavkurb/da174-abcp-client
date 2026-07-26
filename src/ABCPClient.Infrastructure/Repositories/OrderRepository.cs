using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Application.Services;
using ABCPClient.Domain.Entities;
using ABCPClient.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Infrastructure.Repositories;

/// <summary>
/// Доступ к заказам в локальной базе SQLite.
/// </summary>
public sealed class OrderRepository : IOrderRepository
{
    private readonly IDbContextFactory<AbcpDbContext> _contextFactory;
    private readonly ILogger<OrderRepository> _logger;

    /// <summary>
    /// Создаёт репозиторий.
    /// </summary>
    public OrderRepository(
        IDbContextFactory<AbcpDbContext> contextFactory,
        ILogger<OrderRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderListItem>> GetListAsync(
        OrderFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Цвет статуса берётся из справочника: в самом заказе его нет.
        Dictionary<int, string?> colors = await context.OrderStatuses
            .AsNoTracking()
            .ToDictionaryAsync(status => status.StatusCode, status => status.Color, cancellationToken)
            .ConfigureAwait(false);

        List<Order> orders = await Apply(context.Orders.AsNoTracking(), filter)
            .OrderByDescending(order => order.DateUpdated ?? order.Date)
            .ThenByDescending(order => order.Id)
            .Skip(Math.Max(0, filter.Skip))
            .Take(Math.Clamp(filter.Take, 1, 5000))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return orders
            .Select(order => new OrderListItem(
                order.Number,
                order.InternalNumber,
                order.Date,
                order.UserFullName is { Length: > 0 } company ? company : order.UserName,
                BuildStatusText(order),
                order.DominantStatusCode,
                order.DominantStatusCode is { } code && colors.TryGetValue(code, out string? color) ? color : null,
                order.HasMixedStatuses,
                order.Sum,
                order.PositionsQuantity,
                order.DateUpdated,
                order.IsPaid,
                order.IsDeleted))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(OrderFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await Apply(context.Orders.AsNoTracking(), filter)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Order?> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Number == number, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OrderChangeSet> UpsertAsync(
        IReadOnlyCollection<OrderDto> orders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orders);

        if (orders.Count == 0)
        {
            return OrderChangeSet.Empty;
        }

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        string[] numbers = orders.Select(order => order.Number).Distinct(StringComparer.Ordinal).ToArray();

        Dictionary<string, Order> existing = await context.Orders
            .Include(order => order.Items)
            .Where(order => numbers.Contains(order.Number))
            .ToDictionaryAsync(order => order.Number, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        List<string> created = [];
        List<string> updated = [];
        List<OrderStatusChange> statusChanges = [];
        DateTime syncedAt = DateTime.Now;

        foreach (OrderDto dto in orders)
        {
            if (existing.TryGetValue(dto.Number, out Order? order))
            {
                bool changed = HasChanged(order, dto);

                OrderMapper.Apply(dto, order, syncedAt);
                MergePositions(order, dto, statusChanges);
                CascadeDeletionToPositions(order);
                order.RefreshStatusAggregate();

                if (changed)
                {
                    updated.Add(dto.Number);
                }
            }
            else
            {
                order = OrderMapper.ToEntity(dto, syncedAt);
                MergePositions(order, dto, statusChanges: null);
                CascadeDeletionToPositions(order);
                order.RefreshStatusAggregate();

                context.Orders.Add(order);
                created.Add(dto.Number);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Применено заказов: новых {Created}, обновлённых {Updated}, смен статусов {StatusChanges}",
            created.Count,
            updated.Count,
            statusChanges.Count);

        return new OrderChangeSet(created, updated, statusChanges);
    }

    /// <inheritdoc />
    public async Task<DateTime?> GetMaxDateUpdatedAsync(CancellationToken cancellationToken = default)
    {
        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.Orders
            .AsNoTracking()
            .MaxAsync(order => order.DateUpdated, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActiveOrderRef>> GetActiveOrderRefsAsync(
        CancellationToken cancellationToken = default)
    {
        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.Orders
            .AsNoTracking()
            .Where(order => !order.IsDeleted)
            .OrderBy(order => order.Date)
            .Select(order => new ActiveOrderRef(order.Number, order.Date))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> MarkDeletedAsync(
        IReadOnlyCollection<string> numbers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(numbers);

        if (numbers.Count == 0)
        {
            return 0;
        }

        string[] distinct = numbers.Distinct(StringComparer.Ordinal).ToArray();

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Order> orders = await context.Orders
            .Include(order => order.Items)
            .Where(order => distinct.Contains(order.Number) && !order.IsDeleted)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Order order in orders)
        {
            order.IsDeleted = true;
            order.SyncedAt = DateTime.Now;

            // Позиции удалённого заказа тоже неактуальны: иначе фильтр по статусу
            // продолжал бы находить заказ через его позиции.
            foreach (OrderItem item in order.Items)
            {
                item.IsDeleted = true;
            }

            order.RefreshStatusAggregate();
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return orders.Count;
    }

    /// <summary>
    /// Применяет фильтр к запросу заказов.
    /// </summary>
    private static IQueryable<Order> Apply(IQueryable<Order> query, OrderFilter filter)
    {
        if (!filter.IncludeDeleted)
        {
            query = query.Where(order => !order.IsDeleted);
        }

        if (filter.StatusCode is { } statusCode)
        {
            query = query.Where(order =>
                order.DominantStatusCode == statusCode
                || order.Items.Any(item => item.StatusCode == statusCode && !item.IsDeleted));
        }

        if (filter.DateFrom is { } from)
        {
            query = query.Where(order => order.Date >= from);
        }

        if (filter.DateTo is { } to)
        {
            // Верхняя граница включает весь указанный день.
            DateTime inclusiveTo = to.Date.AddDays(1).AddTicks(-1);
            query = query.Where(order => order.Date <= inclusiveTo);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            string text = filter.SearchText.Trim();

            query = query.Where(order =>
                EF.Functions.Like(order.Number, $"%{text}%")
                || (order.InternalNumber != null && EF.Functions.Like(order.InternalNumber, $"%{text}%"))
                || (order.UserName != null && EF.Functions.Like(order.UserName, $"%{text}%"))
                || (order.UserFullName != null && EF.Functions.Like(order.UserFullName, $"%{text}%"))
                || order.Items.Any(item =>
                    EF.Functions.Like(item.Number, $"%{text}%")
                    || EF.Functions.Like(item.Brand, $"%{text}%")));
        }

        return query;
    }

    /// <summary>
    /// Текст статуса заказа: сводка по позициям, посчитанная доменом.
    /// </summary>
    private static string BuildStatusText(Order order)
    {
        if (order.DominantStatusCode is null)
        {
            return "Без статуса";
        }

        string name = order.DominantStatusName ?? order.DominantStatusCode.Value.ToString();
        return order.HasMixedStatuses ? $"{name} (смешанный)" : name;
    }

    /// <summary>
    /// Определяет, изменился ли заказ по сравнению с сохранённой версией.
    /// </summary>
    private static bool HasChanged(Order order, OrderDto dto) =>
        order.DateUpdated != dto.DateUpdated
        || order.Sum != dto.Sum
        || order.PositionsQuantity != dto.PositionsQuantity
        || order.IsPaid != dto.Paid
        || order.IsDeleted != dto.IsDeleted;

    /// <summary>
    /// Помечает позиции удалённого заказа удалёнными.
    /// </summary>
    /// <remarks>
    /// API при <c>format=short</c> не присылает позиции, поэтому флаг удаления заказа
    /// нужно распространить самому: иначе фильтр по статусу продолжит находить
    /// удалённый заказ через его позиции.
    /// </remarks>
    private static void CascadeDeletionToPositions(Order order)
    {
        if (!order.IsDeleted)
        {
            return;
        }

        foreach (OrderItem item in order.Items)
        {
            item.IsDeleted = true;
        }
    }

    /// <summary>
    /// Синхронизирует позиции заказа и собирает смены статусов.
    /// </summary>
    /// <param name="order">Заказ в локальной базе.</param>
    /// <param name="dto">Заказ из API.</param>
    /// <param name="statusChanges">
    /// Куда складывать смены статусов; <c>null</c> для новых заказов — там менять нечего.
    /// </param>
    private static void MergePositions(
        Order order,
        OrderDto dto,
        List<OrderStatusChange>? statusChanges)
    {
        // format=short не отдаёт позиции: в этом случае состав заказа не трогаем.
        if (dto.Positions.Count == 0)
        {
            return;
        }

        Dictionary<long, OrderItem> existing = order.Items
            .GroupBy(item => item.PositionId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (OrderPositionDto positionDto in dto.Positions)
        {
            if (existing.TryGetValue(positionDto.Id, out OrderItem? item))
            {
                int? previousCode = item.StatusCode;
                string? previousStatus = item.Status;

                OrderMapper.Apply(positionDto, item);

                if (statusChanges is not null && previousCode != item.StatusCode)
                {
                    statusChanges.Add(new OrderStatusChange(
                        order.Number,
                        item.PositionId,
                        item.Brand,
                        item.Number,
                        previousStatus,
                        previousCode,
                        item.Status,
                        item.StatusCode));

                    item.StatusHistory.Add(new OrderItemStatusHistoryEntry
                    {
                        StatusCode = item.StatusCode ?? 0,
                        Status = item.Status,
                        ChangedAt = item.StatusChangeDate ?? DateTime.Now,
                    });
                }

                continue;
            }

            order.Items.Add(OrderMapper.ToEntity(positionDto));
        }
    }
}
