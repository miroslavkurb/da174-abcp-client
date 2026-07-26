using ABCPClient.Infrastructure.Database;
using ABCPClient.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет создание базы данных и режимы SQLite.
/// </summary>
public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"abcpclient-tests-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Initialize_creates_database_file_and_enables_wal()
    {
        IDbContextFactory<AbcpDbContext> factory = CreateFactory();
        DatabaseInitializer initializer = new(
            factory,
            new ArticleCardRepository(factory),
            NullLogger<DatabaseInitializer>.Instance);

        await initializer.InitializeAsync(CancellationToken.None);

        Assert.True(File.Exists(_databasePath), "Файл базы данных не создан");
        Assert.Equal("wal", await ReadJournalModeAsync());
    }

    [Fact]
    public async Task Initialize_is_idempotent()
    {
        IDbContextFactory<AbcpDbContext> factory = CreateFactory();
        DatabaseInitializer initializer = new(
            factory,
            new ArticleCardRepository(factory),
            NullLogger<DatabaseInitializer>.Instance);

        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        Assert.True(File.Exists(_databasePath));
    }

    [Fact]
    public void Connection_string_enables_foreign_keys_and_shared_cache()
    {
        string connectionString = SqliteConnectionStringFactory.Create(_databasePath);
        SqliteConnectionStringBuilder builder = new(connectionString);

        Assert.Equal(_databasePath, builder.DataSource);
        Assert.Equal(SqliteCacheMode.Shared, builder.Cache);
        Assert.True(builder.ForeignKeys);
        Assert.Equal(SqliteOpenMode.ReadWriteCreate, builder.Mode);
    }

    private IDbContextFactory<AbcpDbContext> CreateFactory()
    {
        DbContextOptions<AbcpDbContext> options = new DbContextOptionsBuilder<AbcpDbContext>()
            .UseSqlite(SqliteConnectionStringFactory.Create(_databasePath))
            .Options;

        return new TestDbContextFactory(options);
    }

    /// <summary>
    /// Минимальная фабрика контекстов для тестов: путь к базе — временный файл.
    /// </summary>
    private sealed class TestDbContextFactory : IDbContextFactory<AbcpDbContext>
    {
        private readonly DbContextOptions<AbcpDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AbcpDbContext> options) => _options = options;

        public AbcpDbContext CreateDbContext() => new(_options);
    }

    private async Task<string> ReadJournalModeAsync()
    {
        await using SqliteConnection connection = new(SqliteConnectionStringFactory.Create(_databasePath));
        await connection.OpenAsync(CancellationToken.None);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        object? result = await command.ExecuteScalarAsync(CancellationToken.None);
        return result?.ToString()?.ToLowerInvariant() ?? string.Empty;
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
}
