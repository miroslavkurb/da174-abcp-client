using ABCPClient.Application.Interfaces;
using ABCPClient.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Infrastructure.Database;

/// <summary>
/// Создаёт и обновляет локальную базу данных SQLite.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbContextFactory<AbcpDbContext> _contextFactory;
    private readonly IArticleCardRepository _articleCards;
    private readonly ILogger<DatabaseInitializer> _logger;

    /// <summary>
    /// Создаёт инициализатор.
    /// </summary>
    /// <param name="contextFactory">Фабрика контекстов базы данных.</param>
    /// <param name="articleCards">Кэш карточек товаров: ему нужен разовый пересчёт ключей.</param>
    /// <param name="logger">Журнал.</param>
    public DatabaseInitializer(
        IDbContextFactory<AbcpDbContext> contextFactory,
        IArticleCardRepository articleCards,
        ILogger<DatabaseInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(articleCards);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _articleCards = articleCards;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        IEnumerable<string> pending = await context.Database
            .GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        string[] pendingList = pending.ToArray();
        if (pendingList.Length > 0)
        {
            _logger.LogInformation(
                "Применение миграций базы данных: {Count} шт. ({Migrations})",
                pendingList.Length,
                string.Join(", ", pendingList));
        }

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        await EnableWriteAheadLoggingAsync(context, cancellationToken).ConfigureAwait(false);

        if (_articleCards is ArticleCardRepository repository)
        {
            int updated = await repository.BackfillMatchKeysAsync(cancellationToken).ConfigureAwait(false);
            if (updated > 0)
            {
                _logger.LogInformation("Пересчитан ключ сопоставления у карточек товаров: {Count}", updated);
            }
        }

        _logger.LogInformation(
            "База данных готова: {DataSource}",
            context.Database.GetDbConnection().DataSource);
    }

    /// <summary>
    /// Включает журнал WAL, чтобы чтение из UI не блокировалось записью фоновой синхронизации.
    /// Режим сохраняется в самом файле базы, поэтому достаточно выставить его один раз.
    /// </summary>
    private static async Task EnableWriteAheadLoggingAsync(
        AbcpDbContext context,
        CancellationToken cancellationToken)
    {
        await context.Database
            .ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken)
            .ConfigureAwait(false);
    }
}
