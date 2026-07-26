using ABCPClient.Application.Configuration;
using ABCPClient.Application.Interfaces;
using ABCPClient.Infrastructure.Database;
using ABCPClient.Infrastructure.Repositories;
using ABCPClient.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет хранилище настроек, шифрование секретов и приоритет базы над файлом конфигурации.
/// </summary>
public sealed class AppSettingsStoreTests : IAsyncLifetime, IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"abcpclient-settings-{Guid.NewGuid():N}.db");

    private readonly DpapiSecretProtector _protector =
        new(NullLogger<DpapiSecretProtector>.Instance);

    private IDbContextFactory<AbcpDbContext> _contextFactory = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        DbContextOptions<AbcpDbContext> options = new DbContextOptionsBuilder<AbcpDbContext>()
            .UseSqlite(SqliteConnectionStringFactory.Create(_databasePath))
            .Options;

        _contextFactory = new TestDbContextFactory(options);

        await using AbcpDbContext context = _contextFactory.CreateDbContext();
        await context.Database.MigrateAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    private AppSettingsStore CreateStore() =>
        new(_contextFactory, _protector, NullLogger<AppSettingsStore>.Instance);

    [Fact]
    public async Task Migrations_create_schema()
    {
        await using AbcpDbContext context = _contextFactory.CreateDbContext();

        IEnumerable<string> applied = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Contains(applied, migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Plain_setting_round_trips()
    {
        AppSettingsStore store = CreateStore();

        await store.SetAsync(AppSettingKeys.ApiBaseUrl, "https://demo.public.api.abcp.ru");

        Assert.Equal("https://demo.public.api.abcp.ru", await store.GetAsync(AppSettingKeys.ApiBaseUrl));
    }

    [Fact]
    public async Task Missing_setting_returns_null()
    {
        AppSettingsStore store = CreateStore();

        Assert.Null(await store.GetAsync("Unknown:Key"));
    }

    [Fact]
    public async Task Protected_setting_is_not_stored_in_plain_text()
    {
        const string passwordHash = "0123456789abcdef0123456789abcdef";
        AppSettingsStore store = CreateStore();

        await store.SetAsync(AppSettingKeys.ApiPasswordMd5, passwordHash, protect: true);

        // Читается обратно в открытом виде...
        Assert.Equal(passwordHash, await store.GetAsync(AppSettingKeys.ApiPasswordMd5));

        // ...но в файле базы лежит шифротекст, а не сам хэш.
        await using AbcpDbContext context = _contextFactory.CreateDbContext();
        Domain.Entities.AppSetting stored = await context.Settings
            .AsNoTracking()
            .SingleAsync(setting => setting.Key == AppSettingKeys.ApiPasswordMd5, CancellationToken.None);

        Assert.True(stored.IsProtected);
        Assert.NotNull(stored.Value);
        Assert.NotEqual(passwordHash, stored.Value);
        Assert.DoesNotContain(passwordHash, stored.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Setting_can_be_overwritten_and_removed()
    {
        AppSettingsStore store = CreateStore();

        await store.SetAsync(AppSettingKeys.ApiLogin, "first");
        await store.SetAsync(AppSettingKeys.ApiLogin, "second");
        Assert.Equal("second", await store.GetAsync(AppSettingKeys.ApiLogin));

        Assert.True(await store.RemoveAsync(AppSettingKeys.ApiLogin));
        Assert.False(await store.RemoveAsync(AppSettingKeys.ApiLogin));
        Assert.Null(await store.GetAsync(AppSettingKeys.ApiLogin));
    }

    [Fact]
    public async Task Database_values_override_appsettings_defaults()
    {
        AppSettingsStore store = CreateStore();
        await store.SetAsync(AppSettingKeys.ApiBaseUrl, "https://real.public.api.abcp.ru/");
        await store.SetAsync(AppSettingKeys.ApiLogin, "api-admin");
        await store.SetAsync(AppSettingKeys.ApiPasswordMd5, "0123456789abcdef0123456789abcdef", protect: true);
        await store.SetAsync(AppSettingKeys.ApiTimeoutSeconds, "60");
        await store.SetAsync(AppSettingKeys.SyncPollingIntervalSeconds, "300");
        await store.SetAsync(AppSettingKeys.SyncNotificationsEnabled, "0");

        AbcpSettingsProvider provider = new(
            store,
            new StaticOptionsMonitor<AbcpApiOptions>(new AbcpApiOptions
            {
                BaseUrl = "https://demo.public.api.abcp.ru",
                Login = "demo",
                TimeoutSeconds = 30,
                RetryCount = 3,
                PageSize = 500,
            }),
            new StaticOptionsMonitor<SyncOptions>(new SyncOptions
            {
                PollingIntervalSeconds = 120,
                NotificationsEnabled = true,
            }),
            new StaticOptionsMonitor<CatalogOptions>(new CatalogOptions()),
            new StaticOptionsMonitor<UpdateOptions>(new UpdateOptions()),
            new StaticOptionsMonitor<PickingOptions>(new PickingOptions()));

        AbcpApiOptions api = await provider.GetApiOptionsAsync();
        SyncOptions sync = await provider.GetSyncOptionsAsync();

        // Завершающий слеш снимается, чтобы пути операций склеивались предсказуемо.
        Assert.Equal("https://real.public.api.abcp.ru", api.BaseUrl);
        Assert.Equal("api-admin", api.Login);
        Assert.Equal("0123456789abcdef0123456789abcdef", api.PasswordMd5);
        Assert.Equal(60, api.TimeoutSeconds);
        Assert.True(api.IsConfigured);

        Assert.Equal(300, sync.PollingIntervalSeconds);
        Assert.False(sync.NotificationsEnabled);
    }

    [Fact]
    public async Task Defaults_are_used_when_database_is_empty()
    {
        AbcpSettingsProvider provider = new(
            CreateStore(),
            new StaticOptionsMonitor<AbcpApiOptions>(new AbcpApiOptions
            {
                BaseUrl = "https://demo.public.api.abcp.ru",
                TimeoutSeconds = 30,
                PageSize = 5000,
            }),
            new StaticOptionsMonitor<SyncOptions>(new SyncOptions()),
            new StaticOptionsMonitor<CatalogOptions>(new CatalogOptions()),
            new StaticOptionsMonitor<UpdateOptions>(new UpdateOptions()),
            new StaticOptionsMonitor<PickingOptions>(new PickingOptions()));

        AbcpApiOptions api = await provider.GetApiOptionsAsync();

        Assert.Equal("https://demo.public.api.abcp.ru", api.BaseUrl);
        Assert.Equal(30, api.TimeoutSeconds);

        // PageSize ограничивается пределом API в 1000 записей на ответ.
        Assert.Equal(AbcpApiOptions.MaxPageSize, api.PageSize);
        Assert.False(api.IsConfigured);
    }

    [Fact]
    public void Protector_returns_null_for_foreign_or_corrupted_value()
    {
        Assert.Null(_protector.Unprotect("not-a-base64-cipher"));
        Assert.Null(_protector.Unprotect(null));
        Assert.Null(_protector.Unprotect(string.Empty));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (string path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Фабрика контекстов для тестов.</summary>
    private sealed class TestDbContextFactory : IDbContextFactory<AbcpDbContext>
    {
        private readonly DbContextOptions<AbcpDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AbcpDbContext> options) => _options = options;

        public AbcpDbContext CreateDbContext() => new(_options);
    }

}
