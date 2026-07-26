using System.Globalization;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;
using ABCPClient.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace ABCPClient.Infrastructure.Repositories;

/// <summary>
/// Задания на сборку в локальной базе.
/// </summary>
public sealed class PickingRepository : IPickingRepository
{
    private readonly IDbContextFactory<AbcpDbContext> _contextFactory;

    /// <summary>Создаёт хранилище.</summary>
    public PickingRepository(IDbContextFactory<AbcpDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PickingTask>> GetAsync(
        PickingTaskFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        IQueryable<PickingTask> query = context.PickingTasks
            .AsNoTracking()
            .Include(task => task.Lines);

        if (filter.OnlyOpen)
        {
            query = query.Where(task =>
                task.Status == PickingTaskStatus.New || task.Status == PickingTaskStatus.InProgress);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            string search = "%" + filter.SearchText.Trim() + "%";

            query = query.Where(task =>
                EF.Functions.Like(task.Number, search)
                || (task.OrderNumber != null && EF.Functions.Like(task.OrderNumber, search))
                || (task.OneCOrderNumber != null && EF.Functions.Like(task.OneCOrderNumber, search))
                || (task.Customer != null && EF.Functions.Like(task.Customer, search)));
        }

        return await query
            .OrderByDescending(task => task.Id)
            .Take(Math.Clamp(filter.Take, 1, 1000))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Задание возвращается отслеживаемым: вызывающий код меняет строки и передаёт
    /// его в <see cref="UpdateAsync"/>. Контекст живёт до конца операции, поэтому
    /// сохранение идёт своим контекстом с повторным присоединением.
    /// </remarks>
    public async Task<PickingTask?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.PickingTasks
            .AsNoTracking()
            .Include(task => task.Lines)
            .FirstOrDefaultAsync(task => task.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetOrdersWithOpenTasksAsync(
        IReadOnlyCollection<string> orderNumbers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderNumbers);

        if (orderNumbers.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        string[] numbers = orderNumbers.ToArray();

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Закрытое и отменённое задание не мешает создать новое: заказ могли
        // дополнить, и его собирают заново.
        List<string> found = await context.PickingTasks
            .AsNoTracking()
            .Where(task => task.OrderNumber != null
                && numbers.Contains(task.OrderNumber)
                && (task.Status == PickingTaskStatus.New || task.Status == PickingTaskStatus.InProgress))
            .Select(task => task.OrderNumber!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return found.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task AddAsync(
        IReadOnlyCollection<PickingTask> tasks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        if (tasks.Count == 0)
        {
            return;
        }

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        context.PickingTasks.AddRange(tasks);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(PickingTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        PickingTask? stored = await context.PickingTasks
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == task.Id, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            throw new InvalidOperationException($"Задание {task.Id} не найдено");
        }

        stored.Status = task.Status;
        stored.StartedAt = task.StartedAt;
        stored.CompletedAt = task.CompletedAt;
        stored.CompletedBy = task.CompletedBy;
        stored.ExportedAt = task.ExportedAt;
        stored.Comment = task.Comment;

        Dictionary<int, PickingTaskLine> incoming = task.Lines.ToDictionary(line => line.Id);

        foreach (PickingTaskLine line in stored.Lines)
        {
            if (!incoming.TryGetValue(line.Id, out PickingTaskLine? source))
            {
                continue;
            }

            // Меняется только факт сборки: состав задания после создания
            // не редактируется, иначе собранное перестало бы соответствовать заказу.
            line.PickedQuantity = source.PickedQuantity;
            line.PickedAt = source.PickedAt;
            line.PickedBy = source.PickedBy;
            line.Availability = source.Availability;
            line.AvailableQuantity = source.AvailableQuantity;
            line.IncomingEta = source.IncomingEta;
            line.StockLocation = source.StockLocation;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> GetLastNumberAsync(string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Номера сравниваются как числа, а не как строки: «СБ-000010» строкой
        // меньше «СБ-000009» только при равной длине, а длина может измениться.
        List<string> numbers = await context.PickingTasks
            .AsNoTracking()
            .Where(task => task.Number.StartsWith(prefix))
            .Select(task => task.Number)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int last = 0;

        foreach (string number in numbers)
        {
            string tail = number[prefix.Length..];

            if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                && value > last)
            {
                last = value;
            }
        }

        return last;
    }
}
