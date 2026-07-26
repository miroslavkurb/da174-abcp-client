using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Entities;
using ABCPClient.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Infrastructure.Repositories;

/// <summary>
/// Хранилище настроек в таблице <c>Settings</c> локальной базы.
/// </summary>
public sealed class AppSettingsStore : IAppSettingsStore
{
    private readonly IDbContextFactory<AbcpDbContext> _contextFactory;
    private readonly ISecretProtector _protector;
    private readonly ILogger<AppSettingsStore> _logger;

    /// <summary>
    /// Создаёт хранилище.
    /// </summary>
    /// <param name="contextFactory">Фабрика контекстов базы данных.</param>
    /// <param name="protector">Шифрование защищённых значений.</param>
    /// <param name="logger">Журнал.</param>
    public AppSettingsStore(
        IDbContextFactory<AbcpDbContext> contextFactory,
        ISecretProtector protector,
        ILogger<AppSettingsStore> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _protector = protector;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        AppSetting? setting = await context.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Key == key, cancellationToken)
            .ConfigureAwait(false);

        return setting is null ? null : Reveal(setting);
    }

    /// <inheritdoc />
    public async Task SetAsync(
        string key,
        string? value,
        bool protect = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        AppSetting? setting = await context.Settings
            .FirstOrDefaultAsync(item => item.Key == key, cancellationToken)
            .ConfigureAwait(false);

        string? storedValue = value is null || !protect
            ? value
            : _protector.Protect(value);

        if (setting is null)
        {
            setting = new AppSetting { Key = key };
            context.Settings.Add(setting);
        }

        setting.Value = storedValue;
        setting.IsProtected = protect && value is not null;
        setting.UpdatedAt = DateTime.Now;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Значение настройки в журнал не пишем: под защищёнными ключами лежат секреты.
        _logger.LogInformation("Настройка сохранена: {Key} (защищено: {IsProtected})", key, setting.IsProtected);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string?>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        List<AppSetting> settings = await context.Settings
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return settings.ToDictionary(setting => setting.Key, Reveal, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using AbcpDbContext context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        AppSetting? setting = await context.Settings
            .FirstOrDefaultAsync(item => item.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (setting is null)
        {
            return false;
        }

        context.Settings.Remove(setting);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Настройка удалена: {Key}", key);
        return true;
    }

    /// <summary>
    /// Возвращает открытое значение настройки.
    /// </summary>
    private string? Reveal(AppSetting setting) =>
        setting.IsProtected ? _protector.Unprotect(setting.Value) : setting.Value;
}
