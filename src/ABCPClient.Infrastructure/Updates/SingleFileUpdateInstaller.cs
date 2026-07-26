using System.Diagnostics;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Infrastructure.Updates;

/// <summary>
/// Установка обновления для раздачи одним исполняемым файлом.
/// </summary>
/// <remarks>
/// Работающий исполняемый файл в Windows нельзя перезаписать, но можно
/// переименовать: система держит открытым сам образ, а не запись в каталоге.
/// На этом и строится установка — старый файл отъезжает в <c>.old</c>, новый
/// встаёт на его место, приложение перезапускается, а <c>.old</c> удаляется
/// при следующем запуске.
/// Сборка, разложенная каталогом, так не обновляется: подменять десятки файлов
/// работающего приложения ненадёжно, и для неё установка недоступна.
/// </remarks>
public sealed class SingleFileUpdateInstaller : IUpdateInstaller
{
    /// <summary>
    /// Файл, по наличию которого рядом с программой видно, что это сборка
    /// под установленный рантайм, а не один самодостаточный файл.
    /// </summary>
    private const string FrameworkDependentMarker = "ABCPClient.UI.dll";

    private const string BackupSuffix = ".old";

    private readonly IAppSettingsStore _store;
    private readonly ILogger<SingleFileUpdateInstaller> _logger;

    /// <summary>Создаёт установщик.</summary>
    public SingleFileUpdateInstaller(IAppSettingsStore store, ILogger<SingleFileUpdateInstaller> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Путь к работающему исполняемому файлу.
    /// </summary>
    /// <remarks>
    /// Именно <see cref="Environment.ProcessPath"/>, а не <c>AppContext.BaseDirectory</c>:
    /// у самодостаточной сборки одним файлом второй указывает на временный каталог
    /// распаковки, а заменять нужно файл на диске.
    /// </remarks>
    internal string? ExecutablePath { get; set; } = Environment.ProcessPath;

    /// <inheritdoc />
    public bool CanInstall => UnavailableReason is null;

    /// <inheritdoc />
    public string? UnavailableReason
    {
        get
        {
            if (ExecutablePath is null || !File.Exists(ExecutablePath))
            {
                return "Не удалось определить путь к исполняемому файлу";
            }

            if (!string.Equals(
                    Path.GetExtension(ExecutablePath),
                    ".exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Программа запущена не из исполняемого файла Windows";
            }

            string? directory = Path.GetDirectoryName(ExecutablePath);
            if (directory is not null && File.Exists(Path.Combine(directory, FrameworkDependentMarker)))
            {
                return "Установлена сборка под .NET Desktop Runtime — обновите её, распаковав архив выпуска";
            }

            return null;
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Установка недоступна либо контрольная сумма обновления не сверена.
    /// </exception>
    public async Task InstallAndRestartAsync(
        DownloadedUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (UnavailableReason is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        // Замена исполняемого файла непроверенным — прямой путь запустить у себя
        // что угодно, поэтому без сверенной суммы установка не выполняется.
        if (!update.ChecksumVerified)
        {
            throw new InvalidOperationException(
                "Контрольная сумма обновления не сверена: в выпуске нет файла сумм. "
                    + "Установите обновление вручную со страницы выпуска");
        }

        if (!File.Exists(update.FilePath))
        {
            throw new InvalidOperationException($"Файл обновления не найден: {update.FilePath}");
        }

        string current = ExecutablePath!;
        string backup = current + BackupSuffix;

        if (File.Exists(backup))
        {
            TryDelete(backup);
        }

        File.Move(current, backup);

        try
        {
            File.Copy(update.FilePath, current, overwrite: false);
        }
        catch
        {
            // Иначе приложения не останется вообще: файл уже переименован.
            File.Move(backup, current, overwrite: true);
            throw;
        }

        await _store.SetAsync(
                AppSettingKeys.UpdatesPendingCleanup,
                backup,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Установлено обновление {Version}, прошлая версия сохранена как {Backup}",
            update.Version,
            backup);

        Process.Start(new ProcessStartInfo(current) { UseShellExecute = true });
    }

    /// <inheritdoc />
    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        string? backup = await _store
            .GetAsync(AppSettingKeys.UpdatesPendingCleanup, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(backup))
        {
            return;
        }

        if (File.Exists(backup) && !TryDelete(backup))
        {
            // Прошлый экземпляр ещё не завершился. Попробуем в следующий раз.
            return;
        }

        await _store
            .RemoveAsync(AppSettingKeys.UpdatesPendingCleanup, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Файл прошлой версии удалён: {Backup}", backup);
    }

    private bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(exception, "Не удалось удалить {Path}", path);
            return false;
        }
    }
}
