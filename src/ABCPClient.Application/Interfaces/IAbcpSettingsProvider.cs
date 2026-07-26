using ABCPClient.Application.Configuration;

namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Действующие настройки приложения: значения из локальной базы,
/// поверх значений по умолчанию из <c>appsettings.json</c>.
/// </summary>
public interface IAbcpSettingsProvider
{
    /// <summary>
    /// Возвращает действующие параметры подключения к API.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<AbcpApiOptions> GetApiOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает действующие параметры синхронизации.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<SyncOptions> GetSyncOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает действующие параметры импорта каталога магазина.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<CatalogOptions> GetCatalogOptionsAsync(CancellationToken cancellationToken = default);
}
