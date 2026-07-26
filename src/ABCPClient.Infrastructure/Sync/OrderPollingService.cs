using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Infrastructure.Sync;

/// <summary>
/// Фоновая служба периодической синхронизации заказов.
/// </summary>
/// <remarks>
/// Интервал перечитывается на каждой итерации: пользователь может изменить его
/// в окне настроек, и перезапуск приложения для этого не нужен.
/// Служба никогда не падает целиком: ошибка одной итерации логируется
/// и не мешает следующей попытке.
/// </remarks>
public sealed class OrderPollingService : BackgroundService
{
    /// <summary>Задержка перед первой синхронизацией, чтобы не тормозить запуск окна.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(3);

    /// <summary>Пауза, если подключение не настроено: чаще проверять смысла нет.</summary>
    private static readonly TimeSpan NotConfiguredDelay = TimeSpan.FromSeconds(60);

    private readonly IOrderSyncService _sync;
    private readonly IAbcpSettingsProvider _settings;
    private readonly ISyncEventBus _eventBus;
    private readonly INotificationService _notifications;
    private readonly ILogger<OrderPollingService> _logger;

    /// <summary>
    /// Создаёт службу опроса.
    /// </summary>
    public OrderPollingService(
        IOrderSyncService sync,
        IAbcpSettingsProvider settings,
        ISyncEventBus eventBus,
        INotificationService notifications,
        ILogger<OrderPollingService> logger)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(logger);

        _sync = sync;
        _settings = settings;
        _eventBus = eventBus;
        _notifications = notifications;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Фоновая синхронизация запущена");

        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

            bool statusCatalogLoaded = false;
            bool deletedReconciled = false;

            while (!stoppingToken.IsCancellationRequested)
            {
                SyncOptions options = await _settings
                    .GetSyncOptionsAsync(stoppingToken)
                    .ConfigureAwait(false);

                if (!options.Enabled)
                {
                    await Task.Delay(NotConfiguredDelay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                AbcpApiOptions apiOptions = await _settings
                    .GetApiOptionsAsync(stoppingToken)
                    .ConfigureAwait(false);

                if (!apiOptions.IsConfigured)
                {
                    _logger.LogDebug("Синхронизация пропущена: подключение к API не настроено");
                    await Task.Delay(NotConfiguredDelay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Справочник статусов нужен для цветов и фильтра в интерфейсе.
                // Обновляется один раз за сеанс работы, а не на каждой итерации.
                if (!statusCatalogLoaded)
                {
                    statusCatalogLoaded = await TryRefreshStatusCatalogAsync(stoppingToken)
                        .ConfigureAwait(false);
                }

                // Сверка удалённых — один раз за сеанс: инкрементальная синхронизация
                // видит удаление только пока заказ попадает в окно по dateUpdated,
                // а удалённый раньше остался бы в базе навсегда.
                if (!deletedReconciled)
                {
                    deletedReconciled = await TryReconcileDeletedAsync(stoppingToken).ConfigureAwait(false);
                }

                await RunIterationAsync(options, stoppingToken).ConfigureAwait(false);

                TimeSpan interval = TimeSpan.FromSeconds(
                    Math.Clamp(options.PollingIntervalSeconds, 15, 3600));

                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка вместе с приложением.
        }

        _logger.LogInformation("Фоновая синхронизация остановлена");
    }

    private async Task RunIterationAsync(SyncOptions options, CancellationToken stoppingToken)
    {
        try
        {
            SyncResult result = await _sync.SyncAsync(stoppingToken).ConfigureAwait(false);

            _eventBus.Publish(result);

            if (result.IsSuccess && options.NotificationsEnabled && result.Changes.HasChanges)
            {
                await _notifications.NotifyAsync(result.Changes, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Ошибка итерации не должна останавливать службу.
            _logger.LogError(exception, "Итерация синхронизации завершилась ошибкой");
        }
    }

    private async Task<bool> TryReconcileDeletedAsync(CancellationToken stoppingToken)
    {
        try
        {
            int marked = await _sync.ReconcileDeletedOrdersAsync(stoppingToken).ConfigureAwait(false);

            if (marked > 0)
            {
                _logger.LogInformation("Сверка с порталом: помечено удалённых заказов {Count}", marked);
            }

            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось сверить удалённые заказы с порталом");
            return false;
        }
    }

    private async Task<bool> TryRefreshStatusCatalogAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _sync.RefreshStatusCatalogAsync(stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось обновить справочник статусов");
            return false;
        }
    }
}
