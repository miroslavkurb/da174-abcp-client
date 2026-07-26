using ABCPClient.Application.DTO;
using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;

namespace ABCPClient.Application.Services;

/// <summary>
/// Преобразование заказов из формата API в сущности домена.
/// </summary>
public static class OrderMapper
{
    /// <summary>
    /// Создаёт новый заказ по данным API.
    /// </summary>
    /// <param name="dto">Заказ из API.</param>
    /// <param name="syncedAt">Момент синхронизации.</param>
    public static Order ToEntity(OrderDto dto, DateTime syncedAt)
    {
        ArgumentNullException.ThrowIfNull(dto);

        Order order = new();
        Apply(dto, order, syncedAt);
        return order;
    }

    /// <summary>
    /// Переносит данные API в существующий заказ, сохраняя локальные идентификаторы.
    /// </summary>
    /// <param name="dto">Заказ из API.</param>
    /// <param name="order">Заказ в локальной базе.</param>
    /// <param name="syncedAt">Момент синхронизации.</param>
    public static void Apply(OrderDto dto, Order order, DateTime syncedAt)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(order);

        order.Number = dto.Number;
        order.InternalNumber = dto.InternalNumber;
        order.ClientOrderNumber = dto.ClientOrderNumber;
        order.UserId = dto.UserId;
        order.UserName = dto.UserName;
        order.UserFullName = dto.UserFullName;
        order.UserEmail = dto.UserEmail;
        order.UserMobile = dto.UserMobile;
        order.UserCode = dto.UserCode;
        order.ProfileId = dto.ProfileId;
        order.ManagerId = dto.ManagerId;
        order.PositionsQuantity = dto.PositionsQuantity;
        order.Sum = dto.Sum;
        order.Debt = dto.Debt;
        order.IsPaid = dto.Paid;
        order.Date = dto.Date;
        order.DateUpdated = dto.DateUpdated;
        order.ShipmentDate = dto.ShipmentDate;
        order.Comment = dto.Comment;
        order.DeliveryAddress = dto.DeliveryAddress;
        order.DeliveryOffice = dto.DeliveryOffice;
        order.DeliveryType = dto.DeliveryType;
        order.DeliveryCost = dto.DeliveryCost;
        order.PaymentType = dto.PaymentType;
        order.IsDeleted = dto.IsDeleted;
        order.SyncedAt = syncedAt;
    }

    /// <summary>
    /// Создаёт позицию заказа по данным API.
    /// </summary>
    /// <param name="dto">Позиция из API.</param>
    public static OrderItem ToEntity(OrderPositionDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        OrderItem item = new();
        Apply(dto, item);
        return item;
    }

    /// <summary>
    /// Переносит данные API в существующую позицию.
    /// </summary>
    /// <param name="dto">Позиция из API.</param>
    /// <param name="item">Позиция в локальной базе.</param>
    public static void Apply(OrderPositionDto dto, OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(item);

        item.PositionId = dto.Id;
        item.Brand = dto.Brand;
        item.BrandFix = dto.BrandFix;
        item.Number = dto.Number;
        item.NumberFix = dto.NumberFix;
        item.Description = dto.Description;
        item.Quantity = dto.Quantity;
        item.QuantityFinal = dto.QuantityFinal;
        item.PriceIn = dto.PriceIn;
        item.PriceOut = dto.PriceOut;
        item.PriceInSiteCurrency = dto.PriceInSiteCurrency;
        item.CurrencyInId = ToCurrency(dto.CurrencyInId);
        item.CurrencyOutId = ToCurrency(dto.CurrencyOutId);
        item.DeadlineHours = dto.Deadline;
        item.DeadlineMaxHours = dto.DeadlineMax;
        item.Status = dto.Status;
        item.StatusCode = dto.StatusCode;
        item.StatusChangeDate = dto.StatusChangeDate;
        item.DateUpdated = dto.DateUpdated;
        item.CancelRequest = ToCancelRequestState(dto.IsCanceled);
        item.IsDeleted = dto.IsDeleted;
        item.DistributorId = dto.DistributorId;
        item.DistributorName = dto.DistributorName;
        item.DistributorType = ToDistributorType(dto.DistributorType);
        item.DistributorOrderId = dto.DistributorOrderId;
        item.RouteId = dto.RouteId;
        item.SupplierCode = dto.SupplierCode;
        item.ItemKey = dto.ItemKey;
        item.Comment = dto.Comment;
        item.CommentAnswer = dto.CommentAnswer;
        item.Weight = dto.Weight;
    }

    /// <summary>
    /// Создаёт элемент справочника статусов по данным API.
    /// </summary>
    /// <param name="dto">Статус из API.</param>
    /// <param name="syncedAt">Момент синхронизации.</param>
    public static OrderStatus ToEntity(OrderStatusDto dto, DateTime syncedAt)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new OrderStatus
        {
            StatusCode = dto.Id,
            Name = dto.Name,
            Comment = dto.Comment,
            Notify = dto.Notify,
            Paid = dto.Paid,
            StartDelivery = dto.StartDelivery,
            Delivery = dto.Delivery,
            PlacingOrder = dto.PlacingOrder,
            Color = dto.Color,
            SyncedAt = syncedAt,
        };
    }

    /// <summary>
    /// Приводит идентификатор валюты API к перечислению, не теряя неизвестные значения.
    /// </summary>
    private static CurrencyId ToCurrency(int? value) =>
        value is null || !Enum.IsDefined(typeof(CurrencyId), value.Value)
            ? CurrencyId.Unknown
            : (CurrencyId)value.Value;

    private static DistributorType ToDistributorType(int? value) =>
        value is null || !Enum.IsDefined(typeof(DistributorType), value.Value)
            ? DistributorType.Unknown
            : (DistributorType)value.Value;

    private static CancelRequestState ToCancelRequestState(int? value) =>
        value is null || !Enum.IsDefined(typeof(CancelRequestState), value.Value)
            ? CancelRequestState.NotRequested
            : (CancelRequestState)value.Value;
}
