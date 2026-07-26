using System.Text.Json;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Serialization;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет разбор ответов API: смешанные типы значений, форматы дат, ошибки.
/// </summary>
public sealed class AbcpJsonTests
{
    [Fact]
    public void Order_with_string_numbers_is_parsed()
    {
        // Поля sum/weight/quantity в ответах ABCP встречаются и числом, и строкой.
        const string json = """
        {
            "number": "75892367",
            "internalNumber": "УТ-000123",
            "positionsQuantity": "2",
            "sum": "1543.50",
            "paid": "1",
            "date": "2026-07-24 12:31:05",
            "dateUpdated": "2026-07-25 09:14:00",
            "isDelete": 0,
            "positions": [
                {
                    "id": "469961941",
                    "brand": "Febi",
                    "number": "01089",
                    "quantity": "2",
                    "quantityFinal": 2,
                    "priceOut": "771.75",
                    "weight": "1.76",
                    "statusCode": "56233",
                    "status": "В работе",
                    "statusChangeDate": "2026-07-25 09:14:00",
                    "isCanceled": "0",
                    "distributorType": "22"
                }
            ]
        }
        """;

        OrderDto? order = JsonSerializer.Deserialize<OrderDto>(json, AbcpJson.Options);

        Assert.NotNull(order);
        Assert.Equal("75892367", order.Number);
        Assert.Equal("УТ-000123", order.InternalNumber);
        Assert.Equal(2, order.PositionsQuantity);
        Assert.Equal(1543.50m, order.Sum);
        Assert.True(order.Paid);
        Assert.False(order.IsDeleted);
        Assert.Equal(new DateTime(2026, 7, 24, 12, 31, 5), order.Date);
        Assert.Equal(DateTimeKind.Unspecified, order.Date!.Value.Kind);

        OrderPositionDto position = Assert.Single(order.Positions);
        Assert.Equal(469961941L, position.Id);
        Assert.Equal(2m, position.Quantity);
        Assert.Equal(771.75m, position.PriceOut);
        Assert.Equal(1.76m, position.Weight);
        Assert.Equal(56233, position.StatusCode);
        Assert.Equal(0, position.IsCanceled);
        Assert.Equal(22, position.DistributorType);
    }

    [Fact]
    public void Paged_response_exposes_total_count()
    {
        const string json = """
        { "items": [ { "number": "1" }, { "number": "2" } ], "count": "1734" }
        """;

        PagedOrdersDto? page = JsonSerializer.Deserialize<PagedOrdersDto>(json, AbcpJson.Options);

        Assert.NotNull(page);
        Assert.Equal(1734, page.Count);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public void Empty_and_zero_dates_become_null()
    {
        const string json = """
        { "number": "1", "date": "", "dateUpdated": "0000-00-00 00:00:00", "shipmentDate": null }
        """;

        OrderDto? order = JsonSerializer.Deserialize<OrderDto>(json, AbcpJson.Options);

        Assert.NotNull(order);
        Assert.Null(order.Date);
        Assert.Null(order.DateUpdated);
        Assert.Null(order.ShipmentDate);
    }

    [Fact]
    public void Status_dictionary_flags_are_parsed()
    {
        const string json = """
        [ { "id": "56233", "name": "В работе", "notify": "1", "paid": 0, "color": "#ff9900" } ]
        """;

        List<OrderStatusDto>? statuses = JsonSerializer.Deserialize<List<OrderStatusDto>>(json, AbcpJson.Options);

        OrderStatusDto status = Assert.Single(statuses!);
        Assert.Equal(56233, status.Id);
        Assert.True(status.Notify);
        Assert.False(status.Paid);
        Assert.Equal("#ff9900", status.Color);
    }

    [Fact]
    public void Api_error_is_parsed()
    {
        const string json = """{ "errorCode": 102, "errorMessage": "User Authentication Error" }""";

        ApiErrorDto? error = JsonSerializer.Deserialize<ApiErrorDto>(json, AbcpJson.Options);

        Assert.NotNull(error);
        Assert.Equal(AbcpErrorCodes.UserAuthenticationError, error.ErrorCode);
        Assert.True(AbcpErrorCodes.IsPermanent(error.ErrorCode));
        Assert.False(AbcpErrorCodes.IsPermanent(AbcpErrorCodes.CacheError));
    }
}
