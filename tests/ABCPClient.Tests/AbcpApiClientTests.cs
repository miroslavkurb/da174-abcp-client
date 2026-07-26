using System.Net;
using System.Text;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Exceptions;
using ABCPClient.Application.Interfaces;
using ABCPClient.Infrastructure.Api;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет клиент API: сборку запроса, пагинацию, разбор ошибок, повторы и маскирование секретов.
/// </summary>
public sealed class AbcpApiClientTests
{
    private const string PasswordHash = "0123456789abcdef0123456789abcdef";

    private static AbcpApiClient CreateClient(
        StubHttpMessageHandler handler,
        out CollectingLogger logger,
        AbcpApiOptions? options = null)
    {
        logger = new CollectingLogger();

        AbcpApiClient client = new(
            new HttpClient(handler),
            new StubSettingsProvider(options ?? new AbcpApiOptions
            {
                BaseUrl = "https://demo.public.api.abcp.ru",
                Login = "api-admin",
                PasswordMd5 = PasswordHash,
                TimeoutSeconds = 5,
                RetryCount = 2,
                PageSize = 500,
            }),
            logger)
        {
            // В тестах повторы не должны занимать реальное время.
            RetryBaseDelay = TimeSpan.Zero,
        };

        return client;
    }

    [Fact]
    public async Task Orders_request_carries_credentials_and_paging_parameters()
    {
        StubHttpMessageHandler handler = new(
            """{ "items": [ { "number": "75892367", "sum": "1543.50" } ], "count": "42" }""");

        AbcpApiClient client = CreateClient(handler, out _);

        OrderPage page = await client.GetOrdersAsync(new OrderQuery
        {
            DateUpdatedStart = new DateTime(2026, 7, 25, 9, 0, 0),
            StatusCodes = [56233, 56234],
            Skip = 500,
            Limit = 500,
            Descending = true,
        });

        Assert.Equal(42, page.TotalCount);
        OrderDto order = Assert.Single(page.Orders);
        Assert.Equal("75892367", order.Number);
        Assert.Equal(1543.50m, order.Sum);

        Uri request = Assert.Single(handler.Requests);
        string query = Uri.UnescapeDataString(request.Query);

        Assert.Equal("/cp/orders", request.AbsolutePath);
        Assert.Contains("userlogin=api-admin", query, StringComparison.Ordinal);
        Assert.Contains($"userpsw={PasswordHash}", query, StringComparison.Ordinal);
        Assert.Contains("dateUpdatedStart=2026-07-25 09:00:00", query, StringComparison.Ordinal);
        Assert.Contains("statusCode[0]=56233", query, StringComparison.Ordinal);
        Assert.Contains("statusCode[1]=56234", query, StringComparison.Ordinal);
        Assert.Contains("skip=500", query, StringComparison.Ordinal);
        Assert.Contains("limit=500", query, StringComparison.Ordinal);
        Assert.Contains("desc=1", query, StringComparison.Ordinal);
        Assert.Contains("format=p", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Limit_is_capped_at_api_maximum()
    {
        StubHttpMessageHandler handler = new("""{ "items": [], "count": 0 }""");
        AbcpApiClient client = CreateClient(handler, out _);

        await client.GetOrdersAsync(new OrderQuery { Limit = 5000 });

        string query = Uri.UnescapeDataString(Assert.Single(handler.Requests).Query);
        Assert.Contains($"limit={AbcpApiOptions.MaxPageSize}", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Internal_numbers_are_ignored_when_numbers_are_present()
    {
        StubHttpMessageHandler handler = new("""{ "items": [], "count": 0 }""");
        AbcpApiClient client = CreateClient(handler, out _);

        await client.GetOrdersAsync(new OrderQuery
        {
            Numbers = ["75892367"],
            InternalNumbers = ["УТ-000123"],
        });

        string query = Uri.UnescapeDataString(Assert.Single(handler.Requests).Query);
        Assert.Contains("numbers[0]=75892367", query, StringComparison.Ordinal);
        Assert.DoesNotContain("internalNumbers", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Password_hash_is_masked_in_logs()
    {
        StubHttpMessageHandler handler = new("""{ "items": [], "count": 0 }""");
        AbcpApiClient client = CreateClient(handler, out CollectingLogger logger);

        await client.GetOrdersAsync(new OrderQuery());

        string log = string.Join('\n', logger.Messages);
        Assert.DoesNotContain(PasswordHash, log, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("userpsw=***", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authentication_error_is_not_retried()
    {
        StubHttpMessageHandler handler = new(
            new StubResponse(HttpStatusCode.Unauthorized,
                """{ "errorCode": 102, "errorMessage": "User Authentication Error" }"""));

        AbcpApiClient client = CreateClient(handler, out _);

        AbcpApiException exception = await Assert.ThrowsAsync<AbcpApiException>(
            () => client.GetStatusesAsync());

        Assert.Equal(AbcpErrorCodes.UserAuthenticationError, exception.ErrorCode);
        Assert.True(exception.IsPermanent);
        Assert.True(exception.IsAuthenticationFailure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Server_error_is_retried_until_success()
    {
        StubHttpMessageHandler handler = new(
            new StubResponse(HttpStatusCode.InternalServerError, "gateway down"),
            new StubResponse(HttpStatusCode.OK, """[ { "id": 1, "name": "Новый" } ]"""));

        AbcpApiClient client = CreateClient(handler, out _);

        IReadOnlyList<OrderStatusDto> statuses = await client.GetStatusesAsync();

        Assert.Single(statuses);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Retries_stop_after_configured_attempts()
    {
        StubHttpMessageHandler handler = new(
            new StubResponse(HttpStatusCode.ServiceUnavailable, "busy"),
            new StubResponse(HttpStatusCode.ServiceUnavailable, "busy"),
            new StubResponse(HttpStatusCode.ServiceUnavailable, "busy"),
            new StubResponse(HttpStatusCode.ServiceUnavailable, "busy"));

        AbcpApiClient client = CreateClient(handler, out _);

        await Assert.ThrowsAsync<AbcpApiException>(() => client.GetStatusesAsync());

        // RetryCount = 2 означает три попытки суммарно.
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Missing_order_returns_null()
    {
        StubHttpMessageHandler handler = new(
            new StubResponse(HttpStatusCode.NotFound,
                """{ "errorCode": 301, "errorMessage": "Object not found" }"""));

        AbcpApiClient client = CreateClient(handler, out _);

        Assert.Null(await client.GetOrderAsync("404404"));
    }

    [Fact]
    public async Task Not_configured_client_throws_before_any_request()
    {
        StubHttpMessageHandler handler = new("{}");
        AbcpApiClient client = CreateClient(handler, out _, new AbcpApiOptions());

        await Assert.ThrowsAsync<AbcpApiNotConfiguredException>(() => client.GetStatusesAsync());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Status_history_is_requested_in_batches_and_grouped_by_position()
    {
        StubHttpMessageHandler handler = new(
            """
            {
                "positions": {
                    "469961941": [
                        { "statusCode": 56233, "status": "В работе", "datetime": "2026-07-25 09:14:00" }
                    ],
                    "162283919": [
                        { "statusCode": 56234, "status": "Заказан", "datetime": "2026-07-24 18:02:11" }
                    ]
                }
            }
            """);

        AbcpApiClient client = CreateClient(handler, out _);

        IReadOnlyDictionary<long, IReadOnlyList<PositionStatusHistoryDto>> history =
            await client.GetStatusHistoryAsync([469961941, 162283919, 469961941]);

        Assert.Equal(2, history.Count);
        Assert.Equal(56233, history[469961941].Single().StatusCode);
        Assert.Equal(new DateTime(2026, 7, 24, 18, 2, 11), history[162283919].Single().DateTime);

        string query = Uri.UnescapeDataString(Assert.Single(handler.Requests).Query);
        Assert.Contains("positionsId[0]=469961941", query, StringComparison.Ordinal);
        Assert.Contains("positionsId[1]=162283919", query, StringComparison.Ordinal);

        // Дубликаты идентификаторов в запрос не попадают.
        Assert.DoesNotContain("positionsId[2]", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_position_list_does_not_call_api()
    {
        StubHttpMessageHandler handler = new("{}");
        AbcpApiClient client = CreateClient(handler, out _);

        Assert.Empty(await client.GetStatusHistoryAsync([]));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Connection_check_reports_success_and_failure()
    {
        StubHttpMessageHandler ok = new("""[ { "id": 1, "name": "Новый" } ]""");
        ConnectionCheckResult success = await CreateClient(ok, out _).CheckConnectionAsync();
        Assert.True(success.IsSuccess);
        Assert.Contains("1", success.Message, StringComparison.Ordinal);

        StubHttpMessageHandler denied = new(
            new StubResponse(HttpStatusCode.Forbidden,
                """{ "errorCode": 103, "errorMessage": "Access denied" }"""));
        ConnectionCheckResult failure = await CreateClient(denied, out _).CheckConnectionAsync();
        Assert.False(failure.IsSuccess);
        Assert.Equal(AbcpErrorCodes.AccessDenied, failure.ErrorCode);

        StubHttpMessageHandler unset = new("{}");
        ConnectionCheckResult notConfigured = await CreateClient(unset, out _, new AbcpApiOptions())
            .CheckConnectionAsync();
        Assert.False(notConfigured.IsSuccess);
        Assert.Contains("не настроено", notConfigured.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Count_request_uses_paged_format_with_single_record()
    {
        StubHttpMessageHandler handler = new("""{ "items": [], "count": 1734 }""");
        AbcpApiClient client = CreateClient(handler, out _);

        int count = await client.GetOrdersCountAsync(new OrderQuery
        {
            Format = OrderQueryFormat.Full,
            Limit = 500,
            Skip = 1000,
        });

        Assert.Equal(1734, count);

        string query = Uri.UnescapeDataString(Assert.Single(handler.Requests).Query);
        Assert.Contains("format=p", query, StringComparison.Ordinal);
        Assert.Contains("limit=1", query, StringComparison.Ordinal);
        Assert.DoesNotContain("skip=1000", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_json_becomes_api_exception()
    {
        StubHttpMessageHandler handler = new("<html>service unavailable</html>");
        AbcpApiClient client = CreateClient(handler, out _);

        AbcpApiException exception = await Assert.ThrowsAsync<AbcpApiException>(
            () => client.GetStatusesAsync());

        Assert.Contains("разобрать ответ", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Ответ подставного обработчика.</summary>
    private sealed record StubResponse(HttpStatusCode StatusCode, string Body);

    /// <summary>Подставной обработчик HTTP: отдаёт заготовленные ответы по порядку.</summary>
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<StubResponse> _responses;
        private readonly StubResponse _last;

        public StubHttpMessageHandler(string body)
            : this(new StubResponse(HttpStatusCode.OK, body))
        {
        }

        public StubHttpMessageHandler(params StubResponse[] responses)
        {
            _responses = new Queue<StubResponse>(responses);
            _last = responses[^1];
        }

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);

            StubResponse response = _responses.Count > 0 ? _responses.Dequeue() : _last;

            return Task.FromResult(new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>Поставщик настроек с фиксированными значениями.</summary>
    private sealed class StubSettingsProvider : IAbcpSettingsProvider
    {
        private readonly AbcpApiOptions _options;

        public StubSettingsProvider(AbcpApiOptions options) => _options = options;

        public Task<AbcpApiOptions> GetApiOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_options);

        public Task<SyncOptions> GetSyncOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncOptions());

        public Task<CatalogOptions> GetCatalogOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogOptions());
    }

    /// <summary>Журнал, собирающий сообщения для проверки маскирования секретов.</summary>
    private sealed class CollectingLogger : ILogger<AbcpApiClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
