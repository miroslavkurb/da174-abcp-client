using System.Net;
using System.Net.Http.Headers;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.Interfaces;
using ABCPClient.Infrastructure.Api;
using ABCPClient.Infrastructure.Database;
using ABCPClient.Infrastructure.Integration;
using ABCPClient.Infrastructure.Repositories;
using ABCPClient.Infrastructure.Security;
using ABCPClient.Infrastructure.Sync;
using ABCPClient.Infrastructure.Updates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ABCPClient.Infrastructure.DependencyInjection;

/// <summary>
/// Регистрация служб инфраструктурного слоя: конфигурация, HTTP, база, репозитории.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Добавляет инфраструктурные службы в контейнер.
    /// </summary>
    /// <param name="services">Контейнер.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <param name="registerBackgroundSync">
    /// Регистрировать фоновую синхронизацию заказов как <c>IHostedService</c>.
    /// </param>
    /// <remarks>
    /// Фоновую синхронизацию отключают мобильные приложения: в MAUI нет хоста,
    /// который запускал бы <c>IHostedService</c>, и такая регистрация тихо
    /// не работала бы. Синхронизацию там запускает экран, когда он открыт.
    /// </remarks>
    public static IServiceCollection AddInfrastructureLayer(
        this IServiceCollection services,
        IConfiguration configuration,
        bool registerBackgroundSync = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions();

        // Настройки читаются из appsettings.json и переопределяются значениями,
        // сохранёнными пользователем в окне настроек (этап 5–6).
        services.Configure<AbcpApiOptions>(configuration.GetSection(AbcpApiOptions.SectionName));
        services.Configure<SyncOptions>(configuration.GetSection(SyncOptions.SectionName));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<CatalogOptions>(configuration.GetSection(CatalogOptions.SectionName));
        services.Configure<UpdateOptions>(configuration.GetSection(UpdateOptions.SectionName));
        services.Configure<PickingOptions>(configuration.GetSection(PickingOptions.SectionName));

        AddApiClient(services);

        AddDatabase(services, configuration);

        // Настройки: значения из базы перекрывают appsettings.json.
        // Секреты под Windows шифруются DPAPI; на других платформах реализацию
        // обязано зарегистрировать само приложение — последняя регистрация побеждает.
#if WINDOWS
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
#else
        services.AddSingleton<ISecretProtector, UnsupportedSecretProtector>();
#endif
        services.AddSingleton<IAppSettingsStore, AppSettingsStore>();
        services.AddSingleton<IAbcpSettingsProvider, AbcpSettingsProvider>();
        services.AddSingleton<IPasswordHasher, Md5PasswordHasher>();

        // Репозитории. Singleton допустим: контекст базы они не держат,
        // а создают короткоживущий через IDbContextFactory.
        services.AddSingleton<IOrderRepository, OrderRepository>();
        services.AddSingleton<IStatusCatalogRepository, StatusCatalogRepository>();
        services.AddSingleton<ISyncLogRepository, SyncLogRepository>();
        services.AddSingleton<IArticleCardRepository, ArticleCardRepository>();
        services.AddSingleton<IPickingRepository, PickingRepository>();

        // Слой интеграции с внешними учётными системами.
        services.AddSingleton<IExchangeProvider, OneCExchangeProvider>();

        // Импорт каталога магазина: заполняет кэш карточек без обращений к API.
        services.AddSingleton<ICatalogImporter, YmlCatalogImporter>();

        // Витрина магазина: карточки деталей под заказ, которых нет в выгрузке каталога.
        // Singleton обязателен — служба ограничивает частоту обращений к сайту.
        services.AddSingleton<IStorefrontArticleSource, StorefrontArticleSource>();

        // Обновления из релизов GitHub.
        services.AddSingleton<IUpdateService, GitHubUpdateService>();
        services.AddSingleton<IUpdateInstaller, SingleFileUpdateInstaller>();

        // Заглушка уведомлений: слой представления заменяет её на Windows Toast.
        // Нужна, чтобы фоновая служба собиралась и без UI (например, в тестах).
        services.TryAddSingleton<INotificationService, LoggingNotificationService>();

        // Фоновая синхронизация.
        if (registerBackgroundSync)
        {
            services.AddHostedService<OrderPollingService>();
        }

        return services;
    }

    /// <summary>
    /// Подключает типизированный клиент API ABCP.
    /// </summary>
    /// <remarks>
    /// <see cref="HttpClient.Timeout"/> отключён намеренно: значение таймаута приходит
    /// из пользовательских настроек и может меняться без перезапуска, поэтому им
    /// управляет сам клиент через токен отмены. Базовый адрес тоже не задаётся здесь —
    /// он хранится в настройках и подставляется при каждом запросе.
    /// </remarks>
    private static void AddApiClient(IServiceCollection services)
    {
        services
            .AddHttpClient<IAbcpApiClient, AbcpApiClient>(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        // Отдельный клиент для CDN изображений: реквизиты API туда не отправляются.
        services.AddHttpClient(ProductImageCache.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Выгрузка каталога скачивается с витрины магазина, а не с API,
        // поэтому реквизиты API ей тоже не нужны. Таймаут больше: файл крупный.
        services.AddHttpClient(YmlCatalogImporter.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        // Страницы витрины: обычный сайт магазина, реквизиты API туда не отправляются.
        services.AddHttpClient(StorefrontArticleSource.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);

            // Витрина отдаёт разметку страницы; без узнаваемого User-Agent
            // некоторые площадки отвечают заглушкой.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ABCPClient/1.0 (+desktop)");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        });

        // API GitHub: обязателен узнаваемый User-Agent, иначе запрос отклоняется.
        // Токен здесь не задаётся — он приходит из настроек при каждом обращении.
        services.AddHttpClient(GitHubUpdateService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ABCPClient-Updater/1.0");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        });

        services.AddSingleton<IProductImageCache, ProductImageCache>();
    }

    /// <summary>
    /// Подключает EF Core с провайдером SQLite.
    /// </summary>
    /// <remarks>
    /// Регистрируется именно <see cref="IDbContextFactory{TContext}"/>: у настольного
    /// приложения нет области запроса, а UI и фоновая служба обращаются к базе
    /// параллельно, поэтому каждый сценарий создаёт свой короткоживущий контекст.
    /// </remarks>
    private static void AddDatabase(IServiceCollection services, IConfiguration configuration)
    {
        DatabaseOptions databaseOptions = configuration
            .GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>() ?? new DatabaseOptions();

        string connectionString = SqliteConnectionStringFactory.Create(databaseOptions);

        services.AddDbContextFactory<AbcpDbContext>(options =>
            options
                .UseSqlite(
                    connectionString,
                    sqlite => sqlite.MigrationsAssembly(typeof(AbcpDbContext).Assembly.FullName))
                .EnableDetailedErrors());

        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
    }
}
