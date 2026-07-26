using ABCPClient.Application.DTO;
using ABCPClient.Domain.Models;

namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Проверка и загрузка обновлений приложения.
/// </summary>
public interface IUpdateService
{
    /// <summary>Версия работающего приложения.</summary>
    AppVersion CurrentVersion { get; }

    /// <summary>
    /// Проверяет, есть ли выпуск новее установленного.
    /// </summary>
    /// <param name="force">
    /// Проверить, даже если с прошлой проверки прошло мало времени.
    /// Так работает проверка по кнопке.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<UpdateCheckResult> CheckAsync(bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Загружает файл обновления и сверяет его контрольную сумму.
    /// </summary>
    /// <param name="update">Обновление из <see cref="CheckAsync"/>.</param>
    /// <param name="progress">Приёмник сведений о ходе загрузки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<DownloadedUpdate> DownloadAsync(
        AvailableUpdate update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Установка загруженного обновления.
/// </summary>
/// <remarks>
/// Отделено от загрузки, потому что установка зависит от того, как приложение
/// разложено на диске, и умеет завершать работу приложения — прикладному слою
/// об этом знать незачем.
/// </remarks>
public interface IUpdateInstaller
{
    /// <summary>
    /// Можно ли заменить установленную версию автоматически.
    /// </summary>
    /// <remarks>
    /// Автоматическая замена рассчитана на раздачу одним файлом. Сборка,
    /// разложенная каталогом, заменяется вручную: подменять десятки файлов
    /// работающего приложения ненадёжно.
    /// </remarks>
    bool CanInstall { get; }

    /// <summary>Почему установка недоступна, если <see cref="CanInstall"/> ложно.</summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// Заменяет исполняемый файл на загруженный и перезапускает приложение.
    /// </summary>
    /// <param name="update">Загруженное и проверенное обновление.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task InstallAndRestartAsync(DownloadedUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Убирает файл прошлой версии, оставшийся после обновления.
    /// </summary>
    /// <remarks>
    /// Работающий исполняемый файл в Windows нельзя удалить, но можно переименовать.
    /// Поэтому обновление переименовывает старый файл, а удаляется он при следующем
    /// запуске — уже новой версией.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task CleanupAsync(CancellationToken cancellationToken = default);
}
