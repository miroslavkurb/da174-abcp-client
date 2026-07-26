namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Подготовка локальной базы данных к работе.
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Создаёт базу при первом запуске, применяет непримененные миграции
    /// и выставляет режимы, необходимые для параллельной работы UI и фоновой синхронизации.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
