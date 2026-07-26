using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DependencyInjection;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Hub;
using ABCPClient.Infrastructure;
using ABCPClient.Infrastructure.DependencyInjection;
using ABCPClient.UI.Services;
using ABCPClient.UI.ViewModels;
using ABCPClient.UI.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace ABCPClient.UI;

/// <summary>
/// Точка входа приложения и корень композиции.
/// </summary>
/// <remarks>
/// Приложение живёт внутри <see cref="IHost"/>: он владеет контейнером DI,
/// конфигурацией, журналом и фоновыми службами (<c>BackgroundService</c>).
/// Окна и модели представления создаются только контейнером, вручную не инстанцируются.
/// </remarks>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    /// <summary>
    /// Собирает хост, поднимает журнал и открывает главное окно.
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        _host = CreateHost();

        ILogger<App> logger = _host.Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Запуск приложения. Каталог данных: {DataDirectory}", AppPaths.DataDirectory);

        AttachGlobalExceptionHandlers(logger);
        BindingErrorTraceListener.Attach(logger);

        await _host.StartAsync().ConfigureAwait(true);

        if (!await InitializeDatabaseAsync(logger).ConfigureAwait(true))
        {
            Shutdown(exitCode: 1);
            return;
        }

        MainWindow window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        // Обслуживание обновлений идёт после показа окна: ни удаление файла прошлой
        // версии, ни обращение к GitHub не должны задерживать запуск.
        _ = MaintainUpdatesAsync(logger);

        base.OnStartup(e);
    }

    /// <summary>
    /// Корректно останавливает хост и сбрасывает буферы журнала.
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.Services.GetRequiredService<ILogger<App>>().LogInformation("Завершение работы приложения");

            await _host.StopAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            _host.Dispose();
            _host = null;
        }

        await Log.CloseAndFlushAsync().ConfigureAwait(true);

        base.OnExit(e);
    }

    /// <summary>
    /// Убирает файл прошлой версии и проверяет наличие новой.
    /// </summary>
    /// <remarks>
    /// Ни то ни другое не критично для работы, поэтому любая ошибка только пишется
    /// в журнал: приложение должно запускаться и без доступа к GitHub.
    /// Проверка не принудительная — она соблюдает интервал между автоматическими
    /// обращениями, чтобы не тратить лимит GitHub на каждый запуск.
    /// </remarks>
    private async Task MaintainUpdatesAsync(ILogger<App> logger)
    {
        try
        {
            await _host!.Services.GetRequiredService<IUpdateInstaller>()
                .CleanupAsync()
                .ConfigureAwait(true);

            UpdateOptions options = await _host.Services.GetRequiredService<IAbcpSettingsProvider>()
                .GetUpdateOptionsAsync()
                .ConfigureAwait(true);

            if (!options.CheckOnStartup)
            {
                return;
            }

            UpdateCheckResult result = await _host.Services.GetRequiredService<IUpdateService>()
                .CheckAsync()
                .ConfigureAwait(true);

            if (result.Update is not { } update)
            {
                return;
            }

            // Уведомление, а не окно: обновление не срочное, и прерывать работу
            // всплывающим окном при каждом запуске незачем.
            await _host.Services.GetRequiredService<INotificationService>()
                .NotifyMessageAsync(
                    "Доступно обновление",
                    $"Версия {update.Version.Display} — откройте «Обновления» на панели инструментов")
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Обслуживание обновлений не выполнено");
        }
    }

    /// <summary>
    /// Готовит локальную базу данных. Без неё работать нельзя, поэтому при ошибке
    /// приложение сообщает пользователю и завершается.
    /// </summary>
    /// <param name="logger">Журнал.</param>
    /// <returns><c>true</c>, если база готова к работе.</returns>
    private async Task<bool> InitializeDatabaseAsync(ILogger<App> logger)
    {
        try
        {
            IDatabaseInitializer initializer = _host!.Services.GetRequiredService<IDatabaseInitializer>();
            await initializer.InitializeAsync().ConfigureAwait(true);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Не удалось подготовить локальную базу данных");
            MessageBox.Show(
                $"Не удалось открыть локальную базу данных.\n\n{exception.Message}",
                "Ошибка запуска",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// Создаёт и настраивает хост приложения.
    /// </summary>
    private static IHost CreateHost()
    {
        // ContentRootPath задаётся явно: по умолчанию хост берёт текущий рабочий каталог,
        // который у GUI-приложения может быть любым (например, каталог ярлыка).
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            // Файл необязателен: он задаёт лишь значения по умолчанию, а действующие
            // настройки живут в локальной базе. Это позволяет распространять
            // один самодостаточный exe без файлов рядом.
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("ABCPCLIENT_");

        ConfigureSerilog(builder.Configuration);

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(dispose: false);

        builder.Services.AddApplicationLayer();
        builder.Services.AddInfrastructureLayer(builder.Configuration);

        // Узел склада: программа выступает управляющей для терминалов сборки,
        // поэтому узел живёт внутри неё и работает с той же базой.
        builder.Services.AddWarehouseHub(builder.Configuration);

        // Уведомления Windows заменяют журнальную заглушку из инфраструктуры.
        builder.Services.AddSingleton<INotificationService, ToastNotificationService>();

        // Слой представления: окна и модели представления.
        builder.Services.AddSingleton<JournalViewModel>();
        builder.Services.AddSingleton<PickingViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        // Окно настроек создаётся заново при каждом открытии, чтобы поля
        // заполнялись действующими значениями.
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SettingsWindow>();
        builder.Services.AddSingleton<Func<SettingsWindow>>(provider =>
            provider.GetRequiredService<SettingsWindow>);

        // Карточка заказа: своё окно и своя модель представления на каждый заказ,
        // чтобы можно было открыть несколько заказов одновременно.
        builder.Services.AddTransient<OrderDetailsViewModel>();
        builder.Services.AddTransient<OrderDetailsWindow>();
        builder.Services.AddSingleton<Func<OrderDetailsWindow>>(provider =>
            provider.GetRequiredService<OrderDetailsWindow>);

        // Окно обновлений: заново при каждом открытии, чтобы проверка шла с чистого
        // состояния, а не показывала результат прошлого раза.
        builder.Services.AddTransient<UpdateViewModel>();
        builder.Services.AddTransient<UpdateWindow>();
        builder.Services.AddSingleton<Func<UpdateWindow>>(provider =>
            provider.GetRequiredService<UpdateWindow>);

        return builder.Build();
    }

    /// <summary>
    /// Настраивает Serilog: уровни берутся из конфигурации, файл журнала — из каталога данных.
    /// </summary>
    private static void ConfigureSerilog(IConfiguration configuration)
    {
        string version = typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        Log.Logger = new LoggerConfiguration()
            // Уровни по умолчанию задаются в коде: файла appsettings.json может не быть
            // рядом с приложением (одиночный self-contained exe), и тогда в журнал
            // сыпался бы весь SQL от EF Core. Секция Serilog в файле, если она есть,
            // переопределяет эти значения.
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("AppVersion", version)
            .Enrich.WithProperty("MachineUser", Environment.UserName)
            .WriteTo.Debug()
            .WriteTo.File(
                path: Path.Combine(AppPaths.LogsDirectory, "abcpclient-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                restrictedToMinimumLevel: LogEventLevel.Debug,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    /// <summary>
    /// Перехватывает необработанные исключения из UI-потока, пула потоков и задач.
    /// </summary>
    private void AttachGlobalExceptionHandlers(ILogger<App> logger)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            logger.LogError(args.Exception, "Необработанное исключение в UI-потоке");
            MessageBox.Show(
                args.Exception.Message,
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.LogCritical(args.ExceptionObject as Exception, "Необработанное исключение вне UI-потока");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.LogError(args.Exception, "Необработанное исключение в задаче");
            args.SetObserved();
        };
    }
}
