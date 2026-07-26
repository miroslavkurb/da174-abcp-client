using ABCPClient.Application.DependencyInjection;
using ABCPClient.Application.Interfaces;
using ABCPClient.Infrastructure.DependencyInjection;
using ABCPClient.Mobile.Services;
using ABCPClient.Mobile.ViewModels;
using ABCPClient.Mobile.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Mobile;

/// <summary>
/// Корень композиции мобильного приложения.
/// </summary>
/// <remarks>
/// Слои Domain, Application и Infrastructure те же, что у настольной программы:
/// клиент API, локальная база и кэш карточек переиспользуются целиком.
/// Отличаются две вещи — шифрование секретов (на Android нет DPAPI) и отсутствие
/// фоновой синхронизации: в MAUI нет хоста, который запускал бы
/// <c>IHostedService</c>, поэтому синхронизацию запускает экран заказов.
/// </remarks>
public static class MauiProgram
{
    /// <summary>Собирает приложение.</summary>
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Configuration.AddInMemoryCollection(Defaults());

#if DEBUG
        builder.Logging.AddDebug();
#endif
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services.AddApplicationLayer();

        // Фоновая синхронизация выключена намеренно: запускать её нечему.
        builder.Services.AddInfrastructureLayer(builder.Configuration, registerBackgroundSync: false);

        // Шифрование секретов под Android. Регистрация идёт после инфраструктуры,
        // поэтому перекрывает её заглушку: при разрешении одиночной зависимости
        // побеждает последняя регистрация.
        builder.Services.AddSingleton<SecureStorageSecretProtector>();
        builder.Services.AddSingleton<ISecretProtector>(provider =>
            provider.GetRequiredService<SecureStorageSecretProtector>());

        builder.Services.AddSingleton<AppStartup>();

        builder.Services.AddSingleton<OrdersViewModel>();
        builder.Services.AddSingleton<ScanViewModel>();
        builder.Services.AddSingleton<MobileSettingsViewModel>();
        builder.Services.AddTransient<OrderDetailsViewModel>();

        builder.Services.AddSingleton<OrdersPage>();
        builder.Services.AddSingleton<ScanPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddTransient<OrderDetailsPage>();

        // Оболочка тоже создаётся контейнером: ей нужны страницы. Singleton
        // обязателен — в её конструкторе регистрируется маршрут карточки заказа,
        // а повторная регистрация того же маршрута считается ошибкой.
        builder.Services.AddSingleton<AppShell>();

        return builder.Build();
    }

    /// <summary>
    /// Значения настроек по умолчанию.
    /// </summary>
    /// <remarks>
    /// Заданы в коде, а не файлом: у приложения на Android нет каталога рядом
    /// с исполняемым файлом, куда можно положить <c>appsettings.json</c>.
    /// Действующие значения всё равно живут в локальной базе.
    /// </remarks>
    private static Dictionary<string, string?> Defaults() => new(StringComparer.Ordinal)
    {
        ["Abcp:TimeoutSeconds"] = "30",
        ["Abcp:RetryCount"] = "3",
        ["Abcp:PageSize"] = "500",

        ["Sync:Enabled"] = "false",
        ["Sync:PollingIntervalSeconds"] = "300",
        ["Sync:OverlapMinutes"] = "5",
        ["Sync:InitialSyncDays"] = "30",
        ["Sync:NotificationsEnabled"] = "false",

        // Лимиты API те же: телефон обращается к тому же сайту.
        ["Sync:ArticleCardRequestsPerMinute"] = "20",
        ["Sync:ArticleCardRequestsPerHour"] = "300",
        ["Sync:ArticleCardRequestsPerDay"] = "1500",
        ["Sync:ArticleCardCooldownMinutes"] = "15",

        ["Catalog:StorefrontUrl"] = "https://da174.ru",
        ["Catalog:StorefrontRequestsPerMinute"] = "60",

        ["Database:FileName"] = "abcpclient.db",

        // Обновления мобильного приложения ставятся не так, как настольного,
        // поэтому проверка здесь выключена.
        ["Updates:Repository"] = string.Empty,
        ["Updates:CheckOnStartup"] = "false",
    };
}
