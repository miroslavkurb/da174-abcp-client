using ABCPClient.Domain.Models;

namespace ABCPClient.Application.DTO;

/// <summary>
/// Чем закончилась проверка обновлений.
/// </summary>
public enum UpdateCheckOutcome
{
    /// <summary>Установлена последняя версия.</summary>
    UpToDate = 0,

    /// <summary>Доступна новая версия.</summary>
    UpdateAvailable = 1,

    /// <summary>Проверка выключена: не задан репозиторий.</summary>
    Disabled = 2,

    /// <summary>Проверку пропустили: прошло слишком мало времени с прошлой.</summary>
    Skipped = 3,

    /// <summary>Проверка не удалась.</summary>
    Failed = 4,
}

/// <summary>
/// Найденное обновление.
/// </summary>
/// <param name="Version">Версия выпуска.</param>
/// <param name="TagName">Тег выпуска.</param>
/// <param name="Title">Заголовок выпуска.</param>
/// <param name="Notes">Заметки к выпуску.</param>
/// <param name="PublishedAt">Когда выпуск опубликован.</param>
/// <param name="IsPrerelease">Выпуск помечен предварительным.</param>
/// <param name="AssetName">Имя файла обновления.</param>
/// <param name="AssetSize">Размер файла обновления в байтах.</param>
/// <param name="AssetUrl">Адрес загрузки файла через API GitHub.</param>
/// <param name="ChecksumUrl">Адрес файла контрольных сумм или <c>null</c>.</param>
/// <param name="ReleaseUrl">Страница выпуска для человека.</param>
public sealed record AvailableUpdate(
    AppVersion Version,
    string TagName,
    string? Title,
    string? Notes,
    DateTimeOffset? PublishedAt,
    bool IsPrerelease,
    string AssetName,
    long AssetSize,
    string AssetUrl,
    string? ChecksumUrl,
    string ReleaseUrl);

/// <summary>
/// Результат проверки обновлений.
/// </summary>
/// <param name="Outcome">Чем закончилась проверка.</param>
/// <param name="CurrentVersion">Установленная версия.</param>
/// <param name="Update">Найденное обновление, если оно есть.</param>
/// <param name="Message">Пояснение для пользователя.</param>
public sealed record UpdateCheckResult(
    UpdateCheckOutcome Outcome,
    AppVersion CurrentVersion,
    AvailableUpdate? Update,
    string? Message = null);

/// <summary>
/// Ход загрузки обновления.
/// </summary>
/// <param name="Stage">Что выполняется сейчас.</param>
/// <param name="BytesReceived">Сколько байт получено.</param>
/// <param name="TotalBytes">Сколько байт всего, если известно.</param>
public sealed record UpdateDownloadProgress(string Stage, long BytesReceived, long? TotalBytes)
{
    /// <summary>Доля загруженного от нуля до единицы или <c>null</c>.</summary>
    public double? Fraction => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value : null;
}

/// <summary>
/// Загруженное и проверенное обновление, готовое к установке.
/// </summary>
/// <param name="FilePath">Путь к загруженному файлу.</param>
/// <param name="Version">Версия обновления.</param>
/// <param name="ChecksumVerified">
/// Контрольная сумма сверена с файлом сумм из выпуска.
/// </param>
public sealed record DownloadedUpdate(string FilePath, AppVersion Version, bool ChecksumVerified);
