using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Application.Services;
using ABCPClient.Domain.Entities;
using ABCPClient.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace ABCPClient.Infrastructure.Repositories;

/// <summary>
/// Справочник статусов в локальной базе.
/// </summary>
public sealed class StatusCatalogRepository : IStatusCatalogRepository
{
    private readonly IDbContextFactory<AbcpDbContext> _contextFactory;

    /// <summary>Создаёт репозиторий.</summary>
    public StatusCatalogRepository(IDbContextFactory<AbcpDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderStatus>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.OrderStatuses
            .AsNoTracking()
            .OrderBy(status => status.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> UpsertAsync(
        IReadOnlyCollection<OrderStatusDto> statuses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        if (statuses.Count == 0)
        {
            return 0;
        }

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, OrderStatus> existing = await context.OrderStatuses
            .ToDictionaryAsync(status => status.StatusCode, cancellationToken)
            .ConfigureAwait(false);

        DateTime syncedAt = DateTime.Now;

        foreach (OrderStatusDto dto in statuses)
        {
            OrderStatus entity = OrderMapper.ToEntity(dto, syncedAt);

            if (existing.TryGetValue(dto.Id, out OrderStatus? stored))
            {
                stored.Name = entity.Name;
                stored.Comment = entity.Comment;
                stored.Notify = entity.Notify;
                stored.Paid = entity.Paid;
                stored.StartDelivery = entity.StartDelivery;
                stored.Delivery = entity.Delivery;
                stored.PlacingOrder = entity.PlacingOrder;
                stored.Color = entity.Color;
                stored.SyncedAt = syncedAt;
                continue;
            }

            context.OrderStatuses.Add(entity);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await context.OrderStatuses.CountAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Журнал синхронизации в локальной базе.
/// </summary>
public sealed class SyncLogRepository : ISyncLogRepository
{
    private readonly IDbContextFactory<AbcpDbContext> _contextFactory;

    /// <summary>Создаёт репозиторий.</summary>
    public SyncLogRepository(IDbContextFactory<AbcpDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task AddAsync(SyncLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        context.SyncLog.Add(entry);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncLogEntry>> GetRecentAsync(
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.SyncLog
            .AsNoTracking()
            .OrderByDescending(entry => entry.StartedAt)
            .Take(Math.Clamp(take, 1, 2000))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
