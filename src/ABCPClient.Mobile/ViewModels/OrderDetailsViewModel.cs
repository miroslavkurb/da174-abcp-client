using System.Collections.ObjectModel;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Mobile.ViewModels;

/// <summary>
/// Модель представления карточки заказа.
/// </summary>
public sealed partial class OrderDetailsViewModel : ObservableObject
{
    private readonly IOrderRepository _orders;
    private readonly IArticleCardService _cards;
    private readonly IProductImageCache _images;
    private readonly ILogger<OrderDetailsViewModel> _logger;

    /// <summary>Позиции заказа.</summary>
    public ObservableCollection<PositionViewModel> Positions { get; } = [];

    /// <summary>Заголовок.</summary>
    [ObservableProperty]
    private string _header = "Заказ";

    /// <summary>Клиент.</summary>
    [ObservableProperty]
    private string? _customer;

    /// <summary>Сводка по статусу.</summary>
    [ObservableProperty]
    private string? _statusText;

    /// <summary>Сумма заказа.</summary>
    [ObservableProperty]
    private string? _sumText;

    /// <summary>Сообщение о состоянии.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Идёт загрузка.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Создаёт модель представления.</summary>
    public OrderDetailsViewModel(
        IOrderRepository orders,
        IArticleCardService cards,
        IProductImageCache images,
        ILogger<OrderDetailsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(logger);

        _orders = orders;
        _cards = cards;
        _images = images;
        _logger = logger;
    }

    /// <summary>
    /// Загружает заказ, затем догружает карточки товаров с изображениями.
    /// </summary>
    /// <param name="number">Онлайн-номер заказа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task LoadAsync(string number, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        IsBusy = true;

        try
        {
            Order? order = await _orders.GetByNumberAsync(number, cancellationToken).ConfigureAwait(true);
            if (order is null)
            {
                StatusMessage = "Заказ не найден в локальной базе";
                return;
            }

            Header = $"№{order.Number}"
                + (order.InternalNumber is { Length: > 0 } internalNumber ? $" / {internalNumber}" : string.Empty);

            Customer = order.UserFullName is { Length: > 0 } company ? company : order.UserName;
            SumText = $"{order.Sum:N2} ₽";

            StatusText = order.DominantStatusName is { Length: > 0 } status
                ? order.HasMixedStatuses ? $"{status} (смешанный)" : status
                : "Без статуса";

            Positions.Clear();
            foreach (OrderItem item in order.Items.OrderBy(item => item.Brand).ThenBy(item => item.Number))
            {
                Positions.Add(new PositionViewModel(item));
            }

            StatusMessage = Positions.Count == 0 ? "У заказа нет позиций" : null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось открыть заказ {Number}", number);
            StatusMessage = $"Ошибка чтения: {exception.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadCardsCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    /// <summary>
    /// Догружает описания и изображения позиций.
    /// </summary>
    [RelayCommand]
    private async Task LoadCardsAsync(CancellationToken cancellationToken)
    {
        if (Positions.Count == 0)
        {
            return;
        }

        try
        {
            ArticleRef[] articles = Positions
                .Select(position => new ArticleRef(position.Brand, position.Number))
                .ToArray();

            ArticleCardsResult cards = await _cards
                .GetCardsAsync(articles, cancellationToken)
                .ConfigureAwait(true);

            foreach (PositionViewModel position in Positions)
            {
                if (!cards.Cards.TryGetValue(
                        new ArticleRef(position.Brand, position.Number).Key,
                        out ArticleCard? card))
                {
                    continue;
                }

                position.Apply(card);

                if (card.ImageName is not { Length: > 0 } image)
                {
                    continue;
                }

                string? path = await _images
                    .GetOrDownloadAsync(image, cancellationToken)
                    .ConfigureAwait(true);

                if (path is not null)
                {
                    position.Image = ImageSource.FromFile(path);
                }
            }

            int withImages = Positions.Count(position => position.Image is not null);
            StatusMessage = withImages == Positions.Count
                ? null
                : $"Изображений: {withImages} из {Positions.Count}";
        }
        catch (Exception exception)
        {
            // Заказ должен открываться и без картинок.
            _logger.LogWarning(exception, "Не удалось получить карточки товаров");
            StatusMessage = "Карточки товаров недоступны";
        }
    }
}

/// <summary>
/// Позиция заказа на экране.
/// </summary>
public sealed partial class PositionViewModel : ObservableObject
{
    /// <summary>Создаёт позицию.</summary>
    /// <param name="item">Позиция заказа из локальной базы.</param>
    public PositionViewModel(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Brand = item.Brand;
        Number = item.Number;
        Description = item.Description;
        Status = item.Status ?? "Без статуса";

        decimal quantity = item.QuantityFinal == 0 ? item.Quantity : item.QuantityFinal;
        QuantityText = quantity == Math.Floor(quantity) ? $"{quantity:N0} шт." : $"{quantity:N2} шт.";
        SumText = $"{item.Total:N2} ₽";

        DeadlineText = item.DeadlineHours is { } hours
            ? hours >= 24 ? $"{hours / 24} дн." : $"{hours} ч"
            : "—";
    }

    /// <summary>Бренд.</summary>
    public string Brand { get; }

    /// <summary>Артикул.</summary>
    public string Number { get; }

    /// <summary>Количество.</summary>
    public string QuantityText { get; }

    /// <summary>Сумма позиции.</summary>
    public string SumText { get; }

    /// <summary>Статус позиции.</summary>
    public string Status { get; }

    /// <summary>Срок поставки.</summary>
    public string DeadlineText { get; }

    /// <summary>Наименование.</summary>
    [ObservableProperty]
    private string? _description;

    /// <summary>Изображение товара.</summary>
    [ObservableProperty]
    private ImageSource? _image;

    /// <summary>Применяет данные карточки товара.</summary>
    /// <param name="card">Карточка из кэша, витрины или API.</param>
    public void Apply(ArticleCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (!string.IsNullOrWhiteSpace(card.Description))
        {
            Description = card.Description;
        }
    }
}
