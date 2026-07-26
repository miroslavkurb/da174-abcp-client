using System.Diagnostics;
using System.Globalization;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Exceptions;
using ABCPClient.Application.Interfaces;
using ABCPClient.Application.Serialization;
using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Application.Services;

/// <summary>
/// Инкрементальная синхронизация заказов с API ABCP.
/// </summary>
/// <remarks>
/// Окно выборки строится по <c>dateUpdatedStart</c>: этот фильтр возвращает и новые,
/// и изменённые заказы, что и требуется для догоняющей синхронизации.
/// Точка продолжения хранится во времени портала (как её отдаёт API), а не по часам
/// локальной машины: расхождение часовых поясов иначе приводило бы к пропускам.
/// Окно берётся с перекрытием (<see cref="SyncOptions.OverlapMinutes"/>), чтобы не терять
/// заказы, обновлённые в ту же секунду, что и предыдущий срез.
/// </remarks>
public sealed class OrderSyncService : IOrderSyncService
{
    /// <summary>Сколько номеров заказов отправлять в одном запросе при сверке.</summary>
    private const int NumbersBatchSize = 100;

    /// <summary>
    /// Длина окна по дате создания при сверке, суток. API отклоняет диапазон
    /// больше года, поэтому окно берётся с запасом.
    /// </summary>
    private const int ReconcileWindowDays = 300;

    private readonly IAbcpApiClient _api;
    private readonly IOrderRepository _orders;
    private readonly IStatusCatalogRepository _statuses;
    private readonly ISyncLogRepository _syncLog;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IAbcpSettingsProvider _settings;
    private readonly ILogger<OrderSyncService> _logger;

    /// <summary>
    /// Создаёт службу синхронизации.
    /// </summary>
    public OrderSyncService(
        IAbcpApiClient api,
        IOrderRepository orders,
        IStatusCatalogRepository statuses,
        ISyncLogRepository syncLog,
        IAppSettingsStore settingsStore,
        IAbcpSettingsProvider settings,
        ILogger<OrderSyncService> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(syncLog);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _api = api;
        _orders = orders;
        _statuses = statuses;
        _syncLog = syncLog;
        _settingsStore = settingsStore;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        long startedAt = Stopwatch.GetTimestamp();
        DateTime startedAtLocal = DateTime.Now;

        AbcpApiOptions apiOptions = await _settings.GetApiOptionsAsync(cancellationToken).ConfigureAwait(false);
        if (!apiOptions.IsConfigured)
        {
            SyncResult skipped = new(
                SyncOutcome.Skipped,
                0,
                OrderChangeSet.Empty,
                null,
                Stopwatch.GetElapsedTime(startedAt),
                "Подключение к API не настроено");

            await WriteLogAsync(skipped, startedAtLocal, cancellationToken).ConfigureAwait(false);
            return skipped;
        }

        SyncOptions syncOptions = await _settings.GetSyncOptionsAsync(cancellationToken).ConfigureAwait(false);
        DateTime windowFrom = await ResolveWindowStartAsync(syncOptions, cancellationToken).ConfigureAwait(false);

        try
        {
            List<OrderDto> fetched = await FetchOrdersAsync(windowFrom, apiOptions, cancellationToken)
                .ConfigureAwait(false);

            OrderChangeSet changes = await _orders.UpsertAsync(fetched, cancellationToken).ConfigureAwait(false);

            await SaveWatermarkAsync(fetched, cancellationToken).ConfigureAwait(false);

            SyncResult result = new(
                SyncOutcome.Success,
                fetched.Count,
                changes,
                windowFrom,
                Stopwatch.GetElapsedTime(startedAt));

            _logger.LogInformation(
                "Синхронизация выполнена: получено {Fetched}, новых {Created}, обновлено {Updated}, смен статусов {StatusChanges}",
                result.OrdersFetched,
                changes.CreatedOrders.Count,
                changes.UpdatedOrders.Count,
                changes.StatusChanges.Count);

            await WriteLogAsync(result, startedAtLocal, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (AbcpApiException exception)
        {
            _logger.LogError(
                exception,
                "Синхронизация не удалась: операция {Operation}, код ошибки {ErrorCode}",
                exception.Operation,
                exception.ErrorCode);

            SyncResult failed = new(
                SyncOutcome.Failed,
                0,
                OrderChangeSet.Empty,
                windowFrom,
                Stopwatch.GetElapsedTime(startedAt),
                exception.Message,
                exception.ErrorCode);

            await WriteLogAsync(failed, startedAtLocal, cancellationToken).ConfigureAwait(false);
            return failed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Синхронизация не удалась");

            SyncResult failed = new(
                SyncOutcome.Failed,
                0,
                OrderChangeSet.Empty,
                windowFrom,
                Stopwatch.GetElapsedTime(startedAt),
                exception.Message);

            await WriteLogAsync(failed, startedAtLocal, cancellationToken).ConfigureAwait(false);
            return failed;
        }
    }

    /// <inheritdoc />
    public async Task<int> RefreshStatusCatalogAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OrderStatusDto> statuses = await _api
            .GetStatusesAsync(cancellationToken)
            .ConfigureAwait(false);

        int saved = await _statuses.UpsertAsync(statuses, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Справочник статусов обновлён: {Count} записей", saved);
        return saved;
    }

    /// <inheritdoc />
    public async Task<int> ReconcileDeletedOrdersAsync(CancellationToken cancellationToken = default)
    {
        AbcpApiOptions apiOptions = await _settings.GetApiOptionsAsync(cancellationToken).ConfigureAwait(false);
        if (!apiOptions.IsConfigured)
        {
            return 0;
        }

        IReadOnlyList<ActiveOrderRef> active = await _orders
            .GetActiveOrderRefsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (active.Count == 0)
        {
            return 0;
        }

        List<string> deleted = [];
        List<string> missing = [];

        foreach ((DateTime from, DateTime to, List<string> numbers) in BuildReconcileWindows(active))
        {
            foreach (string[] batch in numbers.Chunk(NumbersBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                OrderPage page = await _api.GetOrdersAsync(
                    new OrderQuery
                    {
                        Numbers = batch,
                        DateCreatedStart = from,
                        DateCreatedEnd = to,
                        WithDeleted = true,
                        Format = OrderQueryFormat.Short,
                        Limit = AbcpApiOptions.MaxPageSize,
                    },
                    cancellationToken).ConfigureAwait(false);

                Dictionary<string, OrderDto> returned = page.Orders
                    .GroupBy(order => order.Number, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

                foreach (string number in batch)
                {
                    if (returned.TryGetValue(number, out OrderDto? order))
                    {
                        if (order.IsDeleted)
                        {
                            deleted.Add(number);
                        }

                        continue;
                    }

                    missing.Add(number);
                }
            }
        }

        int marked = await _orders.MarkDeletedAsync(deleted, cancellationToken).ConfigureAwait(false);

        if (marked > 0)
        {
            _logger.LogInformation(
                "Помечены удалённые в портале заказы: {Count} ({Numbers})",
                marked,
                string.Join(", ", deleted.Take(20)));
        }

        if (missing.Count > 0)
        {
            // Заказ мог уехать в архив (архивные не приходят без isArchive=1),
            // поэтому удалённым по факту отсутствия его не помечаем.
            _logger.LogWarning(
                "Заказы не найдены в портале при сверке и оставлены как есть: {Count} ({Numbers})",
                missing.Count,
                string.Join(", ", missing.Take(20)));
        }

        return marked;
    }

    /// <summary>
    /// Разбивает заказы на окна по дате создания для сверки.
    /// </summary>
    /// <remarks>
    /// API отклоняет запрос с ошибкой 4 «Диапазон выбора даты создания заказа не должен
    /// превышать 1 год», поэтому заказы группируются в окна не длиннее
    /// <see cref="ReconcileWindowDays"/> суток. Заказы без даты (её может не быть
    /// в ответе) проверяются последним окном, отсчитанным от текущего дня.
    /// </remarks>
    private static List<(DateTime From, DateTime To, List<string> Numbers)> BuildReconcileWindows(
        IReadOnlyList<ActiveOrderRef> active)
    {
        List<(DateTime From, DateTime To, List<string> Numbers)> windows = [];

        List<ActiveOrderRef> dated = active
            .Where(order => order.Date.HasValue)
            .OrderBy(order => order.Date!.Value)
            .ToList();

        int index = 0;
        while (index < dated.Count)
        {
            DateTime windowStart = dated[index].Date!.Value.Date;
            DateTime limit = windowStart.AddDays(ReconcileWindowDays);

            List<string> numbers = [];
            DateTime windowEnd = windowStart;

            while (index < dated.Count && dated[index].Date!.Value.Date <= limit)
            {
                windowEnd = dated[index].Date!.Value.Date;
                numbers.Add(dated[index].Number);
                index++;
            }

            // Границы раздвигаются на сутки, чтобы попасть в интервал независимо
            // от времени внутри дня; суммарный размах остаётся меньше года.
            windows.Add((windowStart.AddDays(-1), windowEnd.AddDays(1), numbers));
        }

        List<string> undated = active
            .Where(order => !order.Date.HasValue)
            .Select(order => order.Number)
            .ToList();

        if (undated.Count > 0)
        {
            DateTime today = DateTime.Now.Date;
            windows.Add((today.AddDays(-ReconcileWindowDays), today.AddDays(1), undated));
        }

        return windows;
    }

    /// <summary>
    /// Читает все страницы заказов, обновлённых с указанного момента.
    /// </summary>
    private async Task<List<OrderDto>> FetchOrdersAsync(
        DateTime windowFrom,
        AbcpApiOptions apiOptions,
        CancellationToken cancellationToken)
    {
        List<OrderDto> fetched = [];
        HashSet<string> seenNumbers = new(StringComparer.Ordinal);

        int pageSize = Math.Clamp(apiOptions.PageSize, 1, AbcpApiOptions.MaxPageSize);
        int skip = 0;
        int total = int.MaxValue;

        while (skip < total)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OrderPage page = await _api.GetOrdersAsync(
                new OrderQuery
                {
                    DateUpdatedStart = windowFrom,
                    Format = OrderQueryFormat.Paged,
                    Limit = pageSize,
                    Skip = skip,

                    // Без withDeleted API не возвращает удалённые заказы и позиции,
                    // и удаление в панели управления выглядело бы как «заказ просто
                    // перестал приходить». Отсутствие заказа в ответе трактовать как
                    // удаление нельзя: архивные заказы тоже не возвращаются по умолчанию.
                    WithDeleted = true,
                },
                cancellationToken).ConfigureAwait(false);

            total = page.TotalCount;

            if (page.Orders.Count == 0)
            {
                break;
            }

            // Заказ может прийти дважды, если во время постраничного чтения его обновили
            // и он сместился в выдаче: дедупликация по номеру.
            foreach (OrderDto order in page.Orders)
            {
                if (seenNumbers.Add(order.Number))
                {
                    fetched.Add(order);
                }
            }

            skip += page.Orders.Count;

            _logger.LogDebug(
                "Прочитано заказов: {Fetched} из {Total} (окно с {WindowFrom:yyyy-MM-dd HH:mm:ss})",
                fetched.Count,
                total,
                windowFrom);
        }

        return fetched;
    }

    /// <summary>
    /// Определяет нижнюю границу окна выборки.
    /// </summary>
    private async Task<DateTime> ResolveWindowStartAsync(
        SyncOptions syncOptions,
        CancellationToken cancellationToken)
    {
        string? saved = await _settingsStore
            .GetAsync(AppSettingKeys.SyncLastSyncAt, cancellationToken)
            .ConfigureAwait(false);

        DateTime? lastSyncAt = AbcpDateTimeConverter.Parse(saved)
            ?? await _orders.GetMaxDateUpdatedAsync(cancellationToken).ConfigureAwait(false);

        if (lastSyncAt is null)
        {
            // Первый запуск: API без фильтра по дате всё равно ограничивает выборку
            // последними 30 днями, поэтому глубина задаётся явно.
            return DateTime.Now.Date.AddDays(-Math.Max(1, syncOptions.InitialSyncDays));
        }

        return lastSyncAt.Value.AddMinutes(-Math.Max(0, syncOptions.OverlapMinutes));
    }

    /// <summary>
    /// Сохраняет точку продолжения — максимальную дату обновления среди полученных заказов.
    /// </summary>
    /// <remarks>
    /// Используется время портала из ответа API. Если заказов в ответе не было,
    /// точка не двигается: иначе можно проскочить период, за который API ещё не отдало данные.
    /// </remarks>
    private async Task SaveWatermarkAsync(
        IReadOnlyCollection<OrderDto> fetched,
        CancellationToken cancellationToken)
    {
        DateTime? maxDateUpdated = fetched
            .Select(order => order.DateUpdated ?? order.Date)
            .Where(date => date.HasValue)
            .Max();

        if (maxDateUpdated is null)
        {
            return;
        }

        await _settingsStore.SetAsync(
            AppSettingKeys.SyncLastSyncAt,
            maxDateUpdated.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            protect: false,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteLogAsync(
        SyncResult result,
        DateTime startedAtLocal,
        CancellationToken cancellationToken)
    {
        SyncLogEntry entry = new()
        {
            Operation = SyncOperation.Orders,
            Outcome = result.Outcome,
            StartedAt = startedAtLocal,
            FinishedAt = startedAtLocal + result.Duration,
            WindowFrom = result.WindowFrom,
            OrdersFetched = result.OrdersFetched,
            OrdersCreated = result.Changes.CreatedOrders.Count,
            OrdersUpdated = result.Changes.UpdatedOrders.Count,
            StatusChanges = result.Changes.StatusChanges.Count,
            ErrorCode = result.ErrorCode,
            Message = result.Message,
        };

        await _syncLog.AddAsync(entry, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Шина событий синхронизации.
/// </summary>
public sealed class SyncEventBus : ISyncEventBus
{
    /// <inheritdoc />
    public event EventHandler<SyncResult>? SyncCompleted;

    /// <inheritdoc />
    public void Publish(SyncResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        SyncCompleted?.Invoke(this, result);
    }
}
