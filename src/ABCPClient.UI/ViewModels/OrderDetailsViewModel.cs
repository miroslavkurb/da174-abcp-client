using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Application.Services;
using ABCPClient.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ABCPClient.UI.ViewModels;

/// <summary>
/// Модель представления карточки заказа: состав позиций с изображениями товаров.
/// </summary>
/// <remarks>
/// Состав заказа читается из локальной базы, а карточки товаров (описания и изображения)
/// докладываются из API: в ответе <c>cp/orders</c> изображений нет.
/// Картинки подгружаются после открытия окна, чтобы список позиций появлялся сразу.
/// </remarks>
public sealed partial class OrderDetailsViewModel : ObservableObject
{
    private readonly IOrderRepository _orders;
    private readonly IArticleCardService _cards;
    private readonly IProductImageCache _images;
    private readonly ILogger<OrderDetailsViewModel> _logger;

    /// <summary>Позиции заказа.</summary>
    public ObservableCollection<OrderPositionViewModel> Positions { get; } = [];

    [ObservableProperty]
    private string _orderNumber = string.Empty;

    [ObservableProperty]
    private string? _header;

    [ObservableProperty]
    private string? _customer;

    [ObservableProperty]
    private string? _deliveryInfo;

    [ObservableProperty]
    private string? _paymentInfo;

    [ObservableProperty]
    private decimal _sum;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private string? _comment;

    [ObservableProperty]
    private bool _isDeleted;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

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
    /// Загружает заказ и его позиции, затем догружает карточки товаров.
    /// </summary>
    /// <param name="number">Онлайн-номер заказа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task LoadAsync(string number, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        OrderNumber = number;
        IsBusy = true;

        try
        {
            Order? order = await _orders.GetByNumberAsync(number, cancellationToken).ConfigureAwait(true);
            if (order is null)
            {
                StatusMessage = "Заказ не найден в локальной базе";
                return;
            }

            Header = $"Заказ №{order.Number}"
                + (order.InternalNumber is { Length: > 0 } internalNumber ? $" / {internalNumber}" : string.Empty)
                + (order.Date is { } date ? $" от {date:dd.MM.yyyy HH:mm}" : string.Empty);

            Customer = order.UserFullName is { Length: > 0 } company
                ? $"{company} ({order.UserName})"
                : order.UserName;

            DeliveryInfo = string.Join(
                ", ",
                new[] { order.DeliveryType, order.DeliveryOffice, order.DeliveryAddress }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            PaymentInfo = order.IsPaid
                ? $"{order.PaymentType} — оплачен"
                : order.Debt is { } debt && debt > 0
                    ? $"{order.PaymentType} — долг {debt:N2}"
                    : order.PaymentType;

            Sum = order.Sum;
            Comment = order.Comment;
            IsDeleted = order.IsDeleted;
            StatusText = order.DominantStatusName is { Length: > 0 } status
                ? order.HasMixedStatuses ? $"{status} (смешанный)" : status
                : "Без статуса";

            Positions.Clear();
            foreach (OrderItem item in order.Items.OrderBy(item => item.Brand).ThenBy(item => item.Number))
            {
                Positions.Add(new OrderPositionViewModel(item));
            }

            StatusMessage = Positions.Count == 0 ? "У заказа нет позиций" : null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось открыть карточку заказа {Number}", number);
            StatusMessage = $"Ошибка чтения заказа: {exception.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadArticleCardsCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    /// <summary>
    /// Догружает карточки товаров: описания, свойства и изображения.
    /// </summary>
    [RelayCommand]
    private async Task LoadArticleCardsAsync(CancellationToken cancellationToken)
    {
        if (Positions.Count == 0)
        {
            return;
        }

        IsBusy = true;

        try
        {
            ArticleRef[] articles = Positions
                .Select(position => new ArticleRef(position.Brand, position.Number))
                .ToArray();

            ArticleCardsResult cards = await _cards
                .GetCardsAsync(articles, cancellationToken)
                .ConfigureAwait(true);

            foreach (OrderPositionViewModel position in Positions)
            {
                if (!cards.Cards.TryGetValue(
                        new ArticleRef(position.Brand, position.Number).Key,
                        out ArticleCard? card))
                {
                    continue;
                }

                position.ApplyCard(card);

                if (string.IsNullOrWhiteSpace(card.ImageName))
                {
                    continue;
                }

                // Каждая картинка загружается отдельно и появляется по мере готовности:
                // ждать весь набор ради первой позиции не нужно.
                string? path = await _images
                    .GetOrDownloadAsync(card.ImageName, cancellationToken)
                    .ConfigureAwait(true);

                position.ImagePath = path;
            }

            int withImages = Positions.Count(position => position.ImagePath is not null);

            string sources = string.Join(
                ", ",
                new[]
                {
                    cards.FromCache > 0 ? $"из кэша {cards.FromCache}" : null,
                    cards.FetchedFromStorefront > 0 ? $"с витрины {cards.FetchedFromStorefront}" : null,
                    cards.FetchedFromApi > 0 ? $"из API {cards.FetchedFromApi}" : null,
                }.Where(part => part is not null));

            StatusMessage = cards.RateLimited
                ? $"Изображений: {withImages} из {Positions.Count}. API ограничил частоту запросов "
                    + $"({cards.NotRequested} без карточки). Укажите адрес витрины в настройках — "
                    + "карточки будут браться с неё, минуя лимит API"
                : cards.NotRequested > 0
                    ? $"Изображений: {withImages} из {Positions.Count}. Отложено карточек: {cards.NotRequested}"
                    : withImages == 0
                        ? "Изображения для позиций не найдены"
                        : $"Изображений загружено: {withImages} из {Positions.Count}"
                            + (sources.Length > 0 ? $" ({sources})" : string.Empty);
        }
        catch (Exception exception)
        {
            // Карточка заказа должна открываться и без картинок.
            _logger.LogWarning(exception, "Не удалось получить карточки товаров заказа {Number}", OrderNumber);
            StatusMessage = $"Карточки товаров недоступны: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>
/// Позиция заказа в карточке.
/// </summary>
public sealed partial class OrderPositionViewModel : ObservableObject
{
    /// <summary>Создаёт модель представления позиции.</summary>
    /// <param name="item">Позиция заказа из локальной базы.</param>
    public OrderPositionViewModel(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        PositionId = item.PositionId;
        Brand = item.Brand;
        Number = item.Number;
        NumberFix = item.NumberFix;
        Description = item.Description;
        Quantity = item.QuantityFinal == 0 ? item.Quantity : item.QuantityFinal;
        PriceOut = item.PriceOut;
        Total = item.Total;
        Status = item.Status ?? "Без статуса";
        StatusChangeDate = item.StatusChangeDate;
        Distributor = item.DistributorName;
        IsDeleted = item.IsDeleted;

        DeadlineText = item.DeadlineHours is { } hours
            ? hours >= 24 ? $"{hours / 24} дн. ({hours} ч)" : $"{hours} ч"
            : "—";

        CancelRequestText = item.CancelRequest switch
        {
            Domain.Models.CancelRequestState.Requested => "Запрошено удаление",
            Domain.Models.CancelRequestState.RejectedByManager => "Удаление отклонено",
            _ => null,
        };
    }

    /// <summary>Идентификатор позиции в портале.</summary>
    public long PositionId { get; }

    /// <summary>Бренд.</summary>
    public string Brand { get; }

    /// <summary>Артикул.</summary>
    public string Number { get; }

    /// <summary>«Очищенный» артикул — в этом виде номер приходит в карточке товара.</summary>
    public string? NumberFix { get; }

    /// <summary>Количество.</summary>
    public decimal Quantity { get; }

    /// <summary>Цена продажи за единицу.</summary>
    public decimal PriceOut { get; }

    /// <summary>Сумма позиции.</summary>
    public decimal Total { get; }

    /// <summary>Статус позиции.</summary>
    public string Status { get; }

    /// <summary>Дата выставления статуса.</summary>
    public DateTime? StatusChangeDate { get; }

    /// <summary>Срок поставки.</summary>
    public string DeadlineText { get; }

    /// <summary>Поставщик.</summary>
    public string? Distributor { get; }

    /// <summary>Позиция удалена.</summary>
    public bool IsDeleted { get; }

    /// <summary>Пояснение по запросу на удаление позиции.</summary>
    public string? CancelRequestText { get; }

    /// <summary>Описание детали: из заказа, затем уточняется карточкой товара.</summary>
    [ObservableProperty]
    private string? _description;

    /// <summary>Путь к локальной копии изображения товара.</summary>
    [ObservableProperty]
    private string? _imagePath;

    /// <summary>Свойства детали из карточки товара.</summary>
    [ObservableProperty]
    private string? _properties;

    /// <summary>
    /// Применяет данные сохранённой карточки товара.
    /// </summary>
    /// <param name="card">Карточка товара из кэша или API.</param>
    public void ApplyCard(ArticleCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (!string.IsNullOrWhiteSpace(card.Description))
        {
            Description = card.Description;
        }

        // В свойствах бывает несколько десятков строк — в списке показываем краткую выжимку.
        string properties = string.Join(
            "; ",
            ArticleCardProperties.Flatten(card.PropertiesJson)
                .Where(property => !string.Equals(property.Key, "Описание", StringComparison.Ordinal))
                .Take(6)
                .Select(property => $"{property.Key}: {property.Value}"));

        Properties = string.IsNullOrWhiteSpace(properties) ? null : properties;
    }
}
