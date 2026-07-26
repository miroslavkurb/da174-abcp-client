using ABCPClient.Application.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ABCPClient.Infrastructure.Database;

/// <summary>
/// Фабрика контекста для инструментов EF Core (<c>dotnet ef migrations</c>).
/// </summary>
/// <remarks>
/// Нужна, чтобы миграции создавались командой из проекта инфраструктуры,
/// без запуска WPF-приложения в качестве startup-проекта.
/// В рантайме приложения не используется.
/// </remarks>
public sealed class AbcpDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AbcpDbContext>
{
    /// <inheritdoc />
    public AbcpDbContext CreateDbContext(string[] args)
    {
        string connectionString = SqliteConnectionStringFactory.Create(new DatabaseOptions());

        DbContextOptions<AbcpDbContext> options = new DbContextOptionsBuilder<AbcpDbContext>()
            .UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(AbcpDbContext).Assembly.FullName))
            .Options;

        return new AbcpDbContext(options);
    }
}
