using ABCPClient.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Mobile.Services;

/// <summary>
/// Подготовка приложения к работе: ключ шифрования и локальная база.
/// </summary>
/// <remarks>
/// У MAUI нет асинхронной точки старта, а обе задачи асинхронные и обязаны
/// выполниться до первого обращения к настройкам. Поэтому подготовка запускается
/// один раз и её результат ждут экраны через <see cref="Ready"/>: повторные
/// ожидания получают уже завершённую задачу.
/// Порядок важен: ключ шифрования нужен раньше базы, иначе сохранённый пароль
/// API прочитать не получится.
/// </remarks>
public sealed class AppStartup
{
    private readonly SecureStorageSecretProtector _protector;
    private readonly IDatabaseInitializer _database;
    private readonly ILogger<AppStartup> _logger;

    private readonly Lazy<Task> _ready;

    /// <summary>Создаёт службу подготовки.</summary>
    public AppStartup(
        SecureStorageSecretProtector protector,
        IDatabaseInitializer database,
        ILogger<AppStartup> logger)
    {
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(logger);

        _protector = protector;
        _database = database;
        _logger = logger;

        _ready = new Lazy<Task>(InitializeAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Ошибка подготовки, если она была.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>Задача подготовки. Ждать можно сколько угодно раз.</summary>
    public Task Ready => _ready.Value;

    private async Task InitializeAsync()
    {
        try
        {
            await _protector.InitializeAsync().ConfigureAwait(false);
            await _database.InitializeAsync().ConfigureAwait(false);

            _logger.LogInformation("Приложение готово к работе");
        }
        catch (Exception exception)
        {
            // Без базы работать нельзя, но падать при запуске тоже нельзя:
            // экран должен показать причину, а не закрыться.
            FailureMessage = exception.Message;
            _logger.LogCritical(exception, "Не удалось подготовить приложение");
        }
    }
}
