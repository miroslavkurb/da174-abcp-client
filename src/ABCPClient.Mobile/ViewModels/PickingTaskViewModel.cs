using System.Collections.ObjectModel;
using System.Globalization;
using ABCPClient.Contracts;
using ABCPClient.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Mobile.ViewModels;

/// <summary>
/// Модель представления состава задания на сборку.
/// </summary>
/// <remarks>
/// Сканирование ищет строку в самом задании, а не в справочнике: сборщику нужно
/// знать, относится ли деталь к этому заказу, а не существует ли она вообще.
/// Отсканированное сразу отмечается собранным — по одной штуке за скан, потому
/// что так и собирают; количество можно поправить руками.
/// </remarks>
public sealed partial class PickingTaskViewModel : ObservableObject
{
    private readonly HubClient _hub;
    private readonly ILogger<PickingTaskViewModel> _logger;

    private int _taskId;

    /// <summary>Строки задания.</summary>
    public ObservableCollection<PickingLineViewModel> Lines { get; } = [];

    /// <summary>Заголовок задания.</summary>
    [ObservableProperty]
    private string _header = "Задание";

    /// <summary>Клиент и номера заказа.</summary>
    [ObservableProperty]
    private string? _subtitle;

    /// <summary>Ввод со сканера.</summary>
    [ObservableProperty]
    private string? _scanInput;

    /// <summary>Сообщение о состоянии.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Последнее действие сканера — показывается крупно.</summary>
    [ObservableProperty]
    private string? _scanResult;

    /// <summary>Скан не нашёл строку в этом задании.</summary>
    [ObservableProperty]
    private bool _scanFailed;

    /// <summary>Идёт обращение к узлу.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    /// <summary>Задание можно закрыть.</summary>
    [ObservableProperty]
    private bool _canComplete;

    /// <summary>Ход сборки.</summary>
    [ObservableProperty]
    private string? _progressText;

    /// <summary>Обращение не идёт — кнопки доступны.</summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>Создаёт модель представления.</summary>
    public PickingTaskViewModel(HubClient hub, ILogger<PickingTaskViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(logger);

        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// Загружает задание с узла.
    /// </summary>
    /// <param name="id">Идентификатор задания.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task LoadAsync(int id, CancellationToken cancellationToken = default)
    {
        _taskId = id;

        await _hub.LoadAsync().ConfigureAwait(true);

        IsBusy = true;

        try
        {
            HubResult<PickingTaskDetails> result = await _hub
                .GetTaskAsync(id, cancellationToken)
                .ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                StatusMessage = result.Error;
                return;
            }

            Apply(result.Value!);
            await LoadImagesAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось открыть задание {Id}", id);
            StatusMessage = $"Ошибка: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Опознаёт отсканированную деталь среди строк задания и отмечает её собранной.
    /// </summary>
    [RelayCommand]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        string input = (ScanInput ?? string.Empty).Trim();
        if (input.Length == 0)
        {
            return;
        }

        PickingLineViewModel? line = Find(input);

        if (line is null)
        {
            // Деталь может существовать, но не относиться к этому заданию —
            // это разные сообщения, и путать их нельзя.
            ScanFailed = true;
            ScanResult = $"{input} — нет в этом задании";
            ScanInput = null;

            return;
        }

        ScanFailed = false;
        ScanInput = null;

        if (line.PickedQuantity >= line.OrderedQuantity)
        {
            ScanResult = $"{line.Title} — уже собрано полностью";
            return;
        }

        await PickAsync(line, line.PickedQuantity + 1m, cancellationToken).ConfigureAwait(true);

        ScanResult = $"{line.Title} — {line.PickedQuantity:N0} из {line.OrderedQuantity:N0}";
    }

    /// <summary>
    /// Отмечает строку собранной полностью.
    /// </summary>
    [RelayCommand]
    private async Task PickAllAsync(PickingLineViewModel? line)
    {
        if (line is null)
        {
            return;
        }

        await PickAsync(line, line.OrderedQuantity, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Снимает отметку о сборке строки.
    /// </summary>
    [RelayCommand]
    private async Task ClearPickAsync(PickingLineViewModel? line)
    {
        if (line is null)
        {
            return;
        }

        await PickAsync(line, 0m, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Закрывает задание.
    /// </summary>
    [RelayCommand]
    private async Task CompleteAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            HubResult<PickingTaskDetails> result = await _hub
                .CompleteAsync(_taskId, null, cancellationToken)
                .ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                StatusMessage = result.Error;
                return;
            }

            Apply(result.Value!);
            ScanResult = "Задание закрыто";
            ScanFailed = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Обновляет задание с узла.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken) =>
        await LoadAsync(_taskId, cancellationToken).ConfigureAwait(true);

    private async Task PickAsync(
        PickingLineViewModel line,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            HubResult<PickingTaskDetails> result = await _hub
                .PickAsync(_taskId, line.Id, quantity, cancellationToken)
                .ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                StatusMessage = result.Error;
                ScanFailed = true;
                ScanResult = result.Error;

                return;
            }

            Apply(result.Value!);
            StatusMessage = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Ищет строку задания по штрихкоду или артикулу.
    /// </summary>
    /// <remarks>
    /// Сначала штрихкод: он даёт однозначное совпадение. Затем артикул без
    /// разделителей — на упаковке он написан иначе, чем в заказе.
    /// </remarks>
    private PickingLineViewModel? Find(string input)
    {
        PickingLineViewModel? byBarcode = Lines.FirstOrDefault(line => line.HasBarcode(input));
        if (byBarcode is not null)
        {
            return byBarcode;
        }

        string normalized = Normalize(input);

        return Lines.FirstOrDefault(line => Normalize(line.Number) == normalized)
            ?? Lines.FirstOrDefault(line => Normalize(line.Number).EndsWith(normalized, StringComparison.Ordinal)
                && normalized.Length >= 4);
    }

    internal static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private void Apply(PickingTaskDetails details)
    {
        Header = $"{details.Task.Number}";

        Subtitle = string.Join(
            "  ·  ",
            new[]
            {
                details.Task.OrderNumber is { Length: > 0 } order ? $"заказ {order}" : null,
                details.Task.OneCOrderNumber is { Length: > 0 } internalNumber ? internalNumber : null,
                details.Task.Customer,
            }.Where(part => !string.IsNullOrWhiteSpace(part)));

        Dictionary<int, ImageSource?> images = Lines.ToDictionary(line => line.Id, line => line.Image);

        Lines.Clear();
        foreach (PickingLine line in details.Lines)
        {
            PickingLineViewModel item = new(line);

            // Картинки уже загружены — переносим, чтобы не качать их снова
            // после каждой отметки.
            if (images.TryGetValue(line.Id, out ImageSource? image))
            {
                item.Image = image;
            }

            Lines.Add(item);
        }

        ProgressText = $"Собрано {details.Task.CompleteLines} из {details.Task.InStockLines} доступных"
            + (details.Task.IncomingLines > 0 ? $", в пути {details.Task.IncomingLines}" : string.Empty);

        CanComplete = details.Task.Status is PickingStatusCodes.New or PickingStatusCodes.InProgress;
    }

    private async Task LoadImagesAsync(CancellationToken cancellationToken)
    {
        foreach (PickingLineViewModel line in Lines)
        {
            if (line.Image is not null || line.ImageName is null)
            {
                continue;
            }

            byte[]? bytes = await _hub.GetImageAsync(line.ImageName, cancellationToken).ConfigureAwait(true);
            if (bytes is null)
            {
                continue;
            }

            line.Image = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
    }
}

/// <summary>
/// Строка задания на экране терминала.
/// </summary>
public sealed partial class PickingLineViewModel : ObservableObject
{
    private readonly string[] _barcodes;

    /// <summary>Создаёт представление строки.</summary>
    /// <param name="line">Строка задания с узла.</param>
    public PickingLineViewModel(PickingLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Id = line.Id;
        Brand = line.Brand;
        Number = line.Number;
        Title = $"{line.Brand} {line.Number}";
        Description = line.Description ?? "Наименование неизвестно";
        OrderedQuantity = line.OrderedQuantity;
        PickedQuantity = line.PickedQuantity;
        ImageName = line.ImageName;
        StockLocation = line.StockLocation;

        _barcodes = line.Barcodes.ToArray();

        IsInStock = line.Availability == AvailabilityCodes.InStock;
        IsIncoming = line.Availability == AvailabilityCodes.Incoming;

        AvailabilityText = line.Availability switch
        {
            AvailabilityCodes.InStock => "в наличии",
            AvailabilityCodes.Incoming => line.IncomingEta is { } eta
                ? $"в пути, ожидается {eta.LocalDateTime:dd.MM}"
                : "в пути",
            _ => "наличие неизвестно",
        };

        QuantityText = string.Create(
            CultureInfo.CurrentCulture,
            $"{line.PickedQuantity:N0} / {line.OrderedQuantity:N0}");

        IsComplete = line.PickedQuantity >= line.OrderedQuantity && line.OrderedQuantity > 0;
    }

    /// <summary>Идентификатор строки.</summary>
    public int Id { get; }

    /// <summary>Бренд.</summary>
    public string Brand { get; }

    /// <summary>Артикул.</summary>
    public string Number { get; }

    /// <summary>Бренд с артикулом.</summary>
    public string Title { get; }

    /// <summary>Наименование.</summary>
    public string Description { get; }

    /// <summary>Заказано.</summary>
    public decimal OrderedQuantity { get; }

    /// <summary>Собрано.</summary>
    public decimal PickedQuantity { get; }

    /// <summary>Собрано и заказано одной строкой.</summary>
    public string QuantityText { get; }

    /// <summary>Наличие словами.</summary>
    public string AvailabilityText { get; }

    /// <summary>Есть на складе.</summary>
    public bool IsInStock { get; }

    /// <summary>В пути.</summary>
    public bool IsIncoming { get; }

    /// <summary>Собрано полностью.</summary>
    public bool IsComplete { get; }

    /// <summary>Место хранения.</summary>
    public string? StockLocation { get; }

    /// <summary>Имя или адрес изображения.</summary>
    public string? ImageName { get; }

    /// <summary>Изображение товара.</summary>
    [ObservableProperty]
    private ImageSource? _image;

    /// <summary>Есть ли у строки такой штрихкод.</summary>
    /// <param name="barcode">Отсканированный код.</param>
    public bool HasBarcode(string barcode) =>
        _barcodes.Any(code => string.Equals(code, barcode, StringComparison.OrdinalIgnoreCase));
}
