using System.Globalization;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Application.Services;

/// <summary>
/// Задания на сборку заказов.
/// </summary>
/// <remarks>
/// Задание — рабочий документ склада, а не учётный: итог сборки фиксируется здесь,
/// а документ в 1С создаёт менеджер. Поэтому своей нумерации в учётной системе
/// задание не занимает.
/// </remarks>
public sealed class PickingService : IPickingService
{
    private readonly IPickingRepository _repository;
    private readonly IOrderRepository _orders;
    private readonly IArticleCardRepository _cards;
    private readonly IAbcpSettingsProvider _settings;
    private readonly ILogger<PickingService> _logger;

    private readonly SemaphoreSlim _numbering = new(1, 1);

    /// <summary>Создаёт службу сборки.</summary>
    public PickingService(
        IPickingRepository repository,
        IOrderRepository orders,
        IArticleCardRepository cards,
        IAbcpSettingsProvider settings,
        ILogger<PickingService> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _orders = orders;
        _cards = cards;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Источник времени. Отдельным свойством — ради предсказуемости тестов.</summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    /// <inheritdoc />
    public async Task<PickingTaskCreationResult> CreateTasksAsync(
        IReadOnlyCollection<string> orderNumbers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderNumbers);

        string[] unique = orderNumbers
            .Where(number => !string.IsNullOrWhiteSpace(number))
            .Select(number => number.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unique.Length == 0)
        {
            return new PickingTaskCreationResult([], [], [], []);
        }

        PickingOptions options = await _settings.GetPickingOptionsAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlySet<string> alreadyOpen = await _repository
            .GetOrdersWithOpenTasksAsync(unique, cancellationToken)
            .ConfigureAwait(false);

        List<PickingTask> created = [];
        List<string> skippedExisting = [];
        List<string> skippedEmpty = [];
        List<string> notFound = [];

        // Номера выдаются под замком: два одновременных нажатия кнопки иначе
        // получили бы один и тот же номер, а он уникален в базе.
        await _numbering.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int next = await _repository
                .GetLastNumberAsync(options.NumberPrefix, cancellationToken)
                .ConfigureAwait(false);

            foreach (string number in unique)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (alreadyOpen.Contains(number))
                {
                    skippedExisting.Add(number);
                    continue;
                }

                Order? order = await _orders.GetByNumberAsync(number, cancellationToken).ConfigureAwait(false);
                if (order is null)
                {
                    notFound.Add(number);
                    continue;
                }

                PickingTask task = await BuildTaskAsync(order, options, ++next, cancellationToken)
                    .ConfigureAwait(false);

                if (task.Lines.Count == 0)
                {
                    // Нечего собирать: все позиции удалены или отменены.
                    skippedEmpty.Add(number);
                    next--;
                    continue;
                }

                created.Add(task);
            }

            if (created.Count > 0)
            {
                await _repository.AddAsync(created, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _numbering.Release();
        }

        _logger.LogInformation(
            "Задания на сборку: создано {Created}, уже были {Existing}, пусты {Empty}, не найдены {NotFound}",
            created.Count,
            skippedExisting.Count,
            skippedEmpty.Count,
            notFound.Count);

        return new PickingTaskCreationResult(
            created.Select(ToListItem).ToArray(),
            skippedExisting,
            skippedEmpty,
            notFound);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PickingTaskListItem>> GetTasksAsync(
        PickingTaskFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IReadOnlyList<PickingTask> tasks = await _repository
            .GetAsync(filter, cancellationToken)
            .ConfigureAwait(false);

        return tasks.Select(ToListItem).ToArray();
    }

    /// <inheritdoc />
    public Task<PickingTask?> GetTaskAsync(int id, CancellationToken cancellationToken = default) =>
        _repository.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Задание или строка не найдены либо задание уже закрыто.
    /// </exception>
    public async Task<PickingTask> RegisterPickAsync(
        PickRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PickingTask task = await RequireTaskAsync(request.TaskId, cancellationToken).ConfigureAwait(false);

        if (task.Status is PickingTaskStatus.Cancelled)
        {
            throw new InvalidOperationException($"Задание {task.Number} отменено");
        }

        PickingTaskLine line = task.Lines.FirstOrDefault(candidate => candidate.Id == request.LineId)
            ?? throw new InvalidOperationException(
                $"Строка {request.LineId} не относится к заданию {task.Number}");

        DateTime moment = Time.GetLocalNow().DateTime;

        line.RegisterPick(request.Quantity, request.PickedBy, moment);

        task.StartedAt ??= moment;
        task.RefreshStatus();

        await _repository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Задание {Number}: {Brand} {Article} собрано {Quantity} из {Ordered} ({By})",
            task.Number,
            line.Brand,
            line.Number,
            line.PickedQuantity,
            line.OrderedQuantity,
            request.PickedBy ?? "без имени");

        return task;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Задание не найдено или отменено.</exception>
    public async Task<PickingTask> CompleteTaskAsync(
        int id,
        string? completedBy,
        CancellationToken cancellationToken = default)
    {
        PickingTask task = await RequireTaskAsync(id, cancellationToken).ConfigureAwait(false);

        if (task.Status == PickingTaskStatus.Cancelled)
        {
            throw new InvalidOperationException($"Задание {task.Number} отменено");
        }

        DateTime moment = Time.GetLocalNow().DateTime;

        // Закрытие — решение человека: он видит, что собрал всё доступное.
        // Поэтому состояние выставляется прямо, а не пересчётом по строкам.
        task.Status = PickingTaskStatus.Picked;
        task.CompletedAt = moment;
        task.CompletedBy = completedBy;
        task.StartedAt ??= moment;

        await _repository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Задание {Number} закрыто: собрано строк {Complete} из {Total} ({By})",
            task.Number,
            task.CompleteLines,
            task.Lines.Count,
            completedBy ?? "без имени");

        return task;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Задание не найдено или уже закрыто.</exception>
    public async Task<PickingTask> CancelTaskAsync(
        int id,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        PickingTask task = await RequireTaskAsync(id, cancellationToken).ConfigureAwait(false);

        if (task.Status == PickingTaskStatus.Picked)
        {
            throw new InvalidOperationException(
                $"Задание {task.Number} уже закрыто, отменять нечего");
        }

        task.Status = PickingTaskStatus.Cancelled;
        task.Comment = string.IsNullOrWhiteSpace(reason) ? task.Comment : reason.Trim();

        await _repository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Задание {Number} отменено: {Reason}", task.Number, reason ?? "без причины");

        return task;
    }

    private async Task<PickingTask> RequireTaskAsync(int id, CancellationToken cancellationToken) =>
        await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Задание {id} не найдено");

    /// <summary>
    /// Собирает задание по заказу.
    /// </summary>
    private async Task<PickingTask> BuildTaskAsync(
        Order order,
        PickingOptions options,
        int sequence,
        CancellationToken cancellationToken)
    {
        DateTime now = Time.GetLocalNow().DateTime;

        PickingTask task = new()
        {
            Number = options.NumberPrefix + sequence.ToString("D6", CultureInfo.InvariantCulture),
            OrderNumber = order.Number,
            OneCOrderNumber = order.InternalNumber,
            Customer = order.UserFullName is { Length: > 0 } company ? company : order.UserName,
            Status = PickingTaskStatus.New,
            CreatedAt = now,
        };

        OrderItem[] items = order.Items
            .Where(item => !options.SkipCancelledPositions
                || (!item.IsDeleted && item.CancelRequest != CancelRequestState.Requested))
            .OrderBy(item => item.Brand)
            .ThenBy(item => item.Number)
            .ToArray();

        if (items.Length == 0)
        {
            return task;
        }

        // Штрихкоды и наименования берутся из кэша карточек: к API здесь
        // не обращаемся — создание задания не должно зависеть от его лимитов.
        IReadOnlyDictionary<string, ArticleCard> cards = await _cards
            .GetAsync(
                items.Select(item => new ArticleRef(item.Brand, item.Number)).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (OrderItem item in items)
        {
            decimal ordered = item.QuantityFinal == 0 ? item.Quantity : item.QuantityFinal;
            cards.TryGetValue(new ArticleRef(item.Brand, item.Number).Key, out ArticleCard? card);

            (StockAvailability availability, DateTime? eta) = ResolveAvailability(item, options, now);

            task.Lines.Add(new PickingTaskLine
            {
                Brand = item.Brand,
                Number = item.Number,
                MatchKey = ArticleKey.Match(item.Brand, item.Number),
                Description = card?.Description ?? item.Description,
                OrderedQuantity = ordered,
                AvailableQuantity = availability == StockAvailability.InStock ? ordered : 0m,
                Availability = availability,
                IncomingEta = eta,
                Barcodes = card?.Barcodes,
                PositionId = item.PositionId,
            });
        }

        task.RefreshStatus();

        return task;
    }

    /// <summary>
    /// Определяет наличие позиции.
    /// </summary>
    /// <remarks>
    /// Пока нет выгрузки остатков из 1С, судить можно только по статусу позиции
    /// и сроку поставки: в ответе API про физическое наличие ничего нет.
    /// Коды статусов задаются в настройках, потому что у каждого сайта они свои.
    /// Когда появятся остатки 1С, они перекроют этот вывод.
    /// </remarks>
    internal static (StockAvailability Availability, DateTime? Eta) ResolveAvailability(
        OrderItem item,
        PickingOptions options,
        DateTime now)
    {
        if (item.StatusCode is { } status)
        {
            if (options.InStockStatusCodes.Contains(status))
            {
                return (StockAvailability.InStock, null);
            }

            if (options.IncomingStatusCodes.Contains(status))
            {
                return (StockAvailability.Incoming, Eta(item, now));
            }
        }

        if (options.TreatDeadlineAsIncoming && item.DeadlineHours is > 0)
        {
            return (StockAvailability.Incoming, Eta(item, now));
        }

        return (StockAvailability.Unknown, null);
    }

    private static DateTime? Eta(OrderItem item, DateTime now) =>
        item.DeadlineHours is > 0 ? now.AddHours(item.DeadlineHours.Value) : null;

    private static PickingTaskListItem ToListItem(PickingTask task) => new(
        task.Id,
        task.Number,
        task.OrderNumber,
        task.OneCOrderNumber,
        task.Customer,
        task.Status,
        task.CreatedAt,
        task.CompletedAt,
        task.Lines.Count,
        task.InStockLines,
        task.IncomingLines,
        task.CompleteLines);
}
