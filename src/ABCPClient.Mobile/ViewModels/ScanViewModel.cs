using System.Collections.ObjectModel;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Entities;
using ABCPClient.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Mobile.ViewModels;

/// <summary>
/// Модель представления экрана сканирования.
/// </summary>
/// <remarks>
/// Задел под терминал сборки. Аппаратные сканеры (Zebra, Urovo и подобные)
/// работают как клавиатура: подставляют строку в поле с фокусом и жмут Enter,
/// поэтому отдельная библиотека не нужна — нужно поле, которое не теряет фокус.
/// На обычных телефонах то же поле заполняется вручную.
/// Поиск по артикулу равноправен со сканированием, а не запасной: штрихкоды
/// известны не для всех деталей, их источник — только выгрузка каталога.
/// </remarks>
public sealed partial class ScanViewModel : ObservableObject
{
    private readonly IArticleLookup _lookup;
    private readonly IProductImageCache _images;
    private readonly AppStartup _startup;
    private readonly ILogger<ScanViewModel> _logger;

    /// <summary>Найденные детали.</summary>
    public ObservableCollection<FoundArticleViewModel> Results { get; } = [];

    /// <summary>Ввод со сканера или с клавиатуры.</summary>
    [ObservableProperty]
    private string? _input;

    /// <summary>Сообщение о состоянии.</summary>
    [ObservableProperty]
    private string? _statusMessage = "Отсканируйте штрихкод или введите артикул";

    /// <summary>Идёт поиск.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Что-то найдено.</summary>
    [ObservableProperty]
    private bool _hasResults;

    /// <summary>Последний ввод опознан по штрихкоду.</summary>
    [ObservableProperty]
    private bool _foundByBarcode;

    /// <summary>Создаёт модель представления.</summary>
    public ScanViewModel(
        IArticleLookup lookup,
        IProductImageCache images,
        AppStartup startup,
        ILogger<ScanViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(logger);

        _lookup = lookup;
        _images = images;
        _startup = startup;
        _logger = logger;
    }

    /// <summary>
    /// Опознаёт деталь по введённой строке.
    /// </summary>
    /// <remarks>
    /// Вызывается по Enter, который дописывает сканер, и по кнопке «Найти».
    /// </remarks>
    [RelayCommand]
    private async Task LookupAsync(CancellationToken cancellationToken)
    {
        await _startup.Ready.ConfigureAwait(true);

        string query = Input ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        IsBusy = true;

        try
        {
            ArticleLookupResult result = await _lookup
                .LookupAsync(query, cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            Results.Clear();
            FoundByBarcode = result.Kind == ArticleLookupKind.Barcode;

            foreach (ArticleCard card in result.Matches)
            {
                Results.Add(new FoundArticleViewModel(card));
            }

            HasResults = Results.Count > 0;

            StatusMessage = result.Kind switch
            {
                ArticleLookupKind.Barcode => $"Штрихкод {result.Input}: найдено",
                ArticleLookupKind.Search when Results.Count == 1 => "Найдена одна деталь",
                ArticleLookupKind.Search => $"Найдено деталей: {Results.Count}",
                ArticleLookupKind.NotFound when result.LooksLikeBarcode =>
                    $"Штрихкод {result.Input} неизвестен. Штрихкоды берутся из выгрузки каталога — "
                        + "загрузите её в настольной программе или найдите деталь по артикулу",
                ArticleLookupKind.NotFound => "Ничего не найдено. Проверьте артикул или загрузите каталог",
                _ => null,
            };

            // Поле очищается только при успехе: неверный ввод удобнее поправить,
            // чем набирать заново.
            if (Results.Count > 0)
            {
                Input = null;
            }

            await LoadImagesAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Поиск детали не удался");
            StatusMessage = $"Ошибка поиска: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Очищает результаты.
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        Input = null;
        Results.Clear();
        HasResults = false;
        FoundByBarcode = false;
        StatusMessage = "Отсканируйте штрихкод или введите артикул";
    }

    /// <summary>
    /// Подгружает изображения найденных деталей.
    /// </summary>
    /// <remarks>
    /// Только те, что уже есть в кэше карточек: экран сканирования не должен
    /// расходовать лимит обращений к API.
    /// </remarks>
    private async Task LoadImagesAsync(CancellationToken cancellationToken)
    {
        foreach (FoundArticleViewModel found in Results)
        {
            if (found.ImageName is not { Length: > 0 } image)
            {
                continue;
            }

            string? path = await _images.GetOrDownloadAsync(image, cancellationToken).ConfigureAwait(true);
            if (path is not null)
            {
                found.Image = ImageSource.FromFile(path);
            }
        }
    }
}

/// <summary>
/// Найденная деталь на экране сканирования.
/// </summary>
public sealed partial class FoundArticleViewModel : ObservableObject
{
    /// <summary>Создаёт представление найденной детали.</summary>
    /// <param name="card">Карточка товара из локального кэша.</param>
    public FoundArticleViewModel(ArticleCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        Brand = card.Brand;
        Number = card.Number;
        Description = card.Description ?? "Наименование неизвестно";
        ImageName = card.ImageName;

        Barcodes = card.Barcodes is { Length: > 0 } codes
            ? codes.Replace(";", ", ", StringComparison.Ordinal)
            : "штрихкод неизвестен";
    }

    /// <summary>Бренд.</summary>
    public string Brand { get; }

    /// <summary>Артикул.</summary>
    public string Number { get; }

    /// <summary>Наименование.</summary>
    public string Description { get; }

    /// <summary>Штрихкоды через запятую.</summary>
    public string Barcodes { get; }

    /// <summary>Имя или адрес изображения.</summary>
    public string? ImageName { get; }

    /// <summary>Изображение товара.</summary>
    [ObservableProperty]
    private ImageSource? _image;
}
