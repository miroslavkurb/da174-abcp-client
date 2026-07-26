using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ABCPClient.UI.ViewModels;

/// <summary>
/// Модель представления окна обновлений.
/// </summary>
/// <remarks>
/// Проверка, загрузка и установка разнесены по шагам, потому что каждый может
/// не состояться отдельно: обновления может не быть, загрузка может не пройти
/// проверку контрольной суммы, а установка недоступна для сборки, разложенной
/// каталогом. На каждом шаге пользователю остаётся ссылка на страницу выпуска.
/// </remarks>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateService _updates;
    private readonly IUpdateInstaller _installer;
    private readonly ILogger<UpdateViewModel> _logger;

    private AvailableUpdate? _available;
    private DownloadedUpdate? _downloaded;

    /// <summary>Создаёт модель представления.</summary>
    public UpdateViewModel(
        IUpdateService updates,
        IUpdateInstaller installer,
        ILogger<UpdateViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(logger);

        _updates = updates;
        _installer = installer;
        _logger = logger;

        CurrentVersion = updates.CurrentVersion.Display;
    }

    /// <summary>Установленная версия.</summary>
    public string CurrentVersion { get; }

    /// <summary>Версия найденного обновления.</summary>
    [ObservableProperty]
    private string? _availableVersion;

    /// <summary>Заметки к выпуску.</summary>
    [ObservableProperty]
    private string? _notes;

    /// <summary>Сообщение о состоянии.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Выполняется проверка, загрузка или установка.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Обновление найдено и его можно загрузить.</summary>
    [ObservableProperty]
    private bool _hasUpdate;

    /// <summary>Обновление загружено и готово к установке.</summary>
    [ObservableProperty]
    private bool _isDownloaded;

    /// <summary>Доля загруженного от нуля до единицы.</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>Показывать индикатор загрузки.</summary>
    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>Ссылка на страницу выпуска.</summary>
    [ObservableProperty]
    private string? _releaseUrl;

    /// <summary>Установка недоступна: пояснение или <c>null</c>.</summary>
    public string? InstallUnavailableReason => _installer.UnavailableReason;

    /// <summary>Установка возможна автоматически.</summary>
    public bool CanInstallAutomatically => _installer.CanInstall;

    /// <summary>
    /// Проверяет наличие обновления.
    /// </summary>
    /// <param name="force">Игнорировать интервал между автоматическими проверками.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<UpdateCheckResult> CheckAsync(bool force, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = "Проверка обновлений…";

        try
        {
            UpdateCheckResult result = await _updates.CheckAsync(force, cancellationToken).ConfigureAwait(true);

            _available = result.Update;
            HasUpdate = result.Update is not null;
            IsDownloaded = false;
            _downloaded = null;

            if (result.Update is { } update)
            {
                AvailableVersion = update.Version.Display;
                Notes = string.IsNullOrWhiteSpace(update.Notes) ? "Заметки к выпуску не заполнены" : update.Notes;
                ReleaseUrl = update.ReleaseUrl;

                StatusMessage = $"Доступна версия {update.Version.Display}"
                    + (update.IsPrerelease ? " (предварительный выпуск)" : string.Empty)
                    + $". Размер файла — {update.AssetSize / 1024d / 1024d:N1} МБ";
            }
            else
            {
                AvailableVersion = null;
                Notes = null;
                ReleaseUrl = null;

                StatusMessage = result.Outcome switch
                {
                    UpdateCheckOutcome.UpToDate => $"Установлена последняя версия ({CurrentVersion})",
                    UpdateCheckOutcome.Disabled =>
                        "Проверка обновлений выключена: укажите репозиторий в настройках",
                    UpdateCheckOutcome.Skipped => result.Message,
                    _ => $"Не удалось проверить обновления: {result.Message}",
                };
            }

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Проверяет обновления по кнопке.
    /// </summary>
    [RelayCommand]
    private async Task CheckNowAsync(CancellationToken cancellationToken) =>
        await CheckAsync(force: true, cancellationToken).ConfigureAwait(true);

    /// <summary>
    /// Загружает файл обновления и сверяет контрольную сумму.
    /// </summary>
    [RelayCommand]
    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        if (_available is null)
        {
            return;
        }

        IsBusy = true;
        IsDownloading = true;
        Progress = 0;

        try
        {
            Progress<UpdateDownloadProgress> progress = new(report =>
            {
                Progress = report.Fraction ?? 0;

                StatusMessage = report.Fraction is { } fraction
                    ? $"{report.Stage}: {fraction * 100:N0}% "
                        + $"({report.BytesReceived / 1024d / 1024d:N1} из {report.TotalBytes / 1024d / 1024d:N1} МБ)"
                    : report.Stage;
            });

            _downloaded = await _updates
                .DownloadAsync(_available, progress, cancellationToken)
                .ConfigureAwait(true);

            IsDownloaded = true;

            StatusMessage = $"Версия {_downloaded.Version.Display} загружена, контрольная сумма сверена. "
                + (CanInstallAutomatically
                    ? "Нажмите «Установить и перезапустить»"
                    : InstallUnavailableReason);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Загрузка отменена";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось загрузить обновление");
            StatusMessage = $"Ошибка загрузки: {exception.Message}";
        }
        finally
        {
            IsDownloading = false;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Устанавливает загруженное обновление и перезапускает приложение.
    /// </summary>
    /// <remarks>
    /// Приложение завершается сразу после запуска новой версии: держать два
    /// экземпляра с одной базой дольше необходимого незачем.
    /// </remarks>
    [RelayCommand]
    private async Task InstallAsync(CancellationToken cancellationToken)
    {
        if (_downloaded is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Установка…";

        try
        {
            await _installer.InstallAndRestartAsync(_downloaded, cancellationToken).ConfigureAwait(true);

            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось установить обновление");
            StatusMessage = $"Ошибка установки: {exception.Message}";
            IsBusy = false;
        }
    }

    /// <summary>
    /// Открывает страницу выпуска в браузере.
    /// </summary>
    [RelayCommand]
    private void OpenReleasePage()
    {
        if (string.IsNullOrWhiteSpace(ReleaseUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(ReleaseUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось открыть страницу выпуска {Url}", ReleaseUrl);
            StatusMessage = $"Не удалось открыть страницу: {exception.Message}";
        }
    }
}
