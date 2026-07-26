using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Exceptions;
using ABCPClient.Application.Interfaces;
using ABCPClient.Application.Serialization;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Infrastructure.Api;

/// <summary>
/// Клиент административного интерфейса API ABCP.
/// </summary>
/// <remarks>
/// Реквизиты (<c>userlogin</c>, <c>userpsw</c>) подставляются в каждый запрос из
/// действующих настроек: своих сессий и токенов у API нет.
/// Таймаут задаётся не свойством <see cref="HttpClient.Timeout"/>, а токеном отмены,
/// потому что значение приходит из настроек и может меняться на ходу.
/// </remarks>
public sealed class AbcpApiClient : IAbcpApiClient
{
    private const string OrdersOperation = "cp/orders";
    private const string OrderOperation = "cp/order";
    private const string StatusesOperation = "cp/statuses";
    private const string StatusHistoryOperation = "cp/orders/statusHistory";
    private const string ArticlesInfoBatchOperation = "cp/articles/info/batch";
    private const string ArticleInfoOperation = "articles/info";

    /// <summary>Максимум идентификаторов позиций в одном пакетном запросе истории статусов.</summary>
    private const int StatusHistoryBatchSize = 100;

    /// <summary>Максимум деталей в одном запросе карточек товара — ограничение API.</summary>
    private const int ArticlesBatchSize = 100;

    private readonly HttpClient _http;
    private readonly IAbcpSettingsProvider _settings;
    private readonly ILogger<AbcpApiClient> _logger;

    /// <summary>
    /// Поддерживает ли сайт пакетное получение карточек товаров.
    /// Выясняется по первому ответу и запоминается на время работы приложения.
    /// </summary>
    private volatile bool _batchArticlesSupported = true;

    /// <summary>
    /// Создаёт клиент.
    /// </summary>
    /// <param name="http">HTTP-клиент из <c>IHttpClientFactory</c>.</param>
    /// <param name="settings">Действующие настройки приложения.</param>
    /// <param name="logger">Журнал.</param>
    public AbcpApiClient(
        HttpClient http,
        IAbcpSettingsProvider settings,
        ILogger<AbcpApiClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Базовая задержка перед повтором. Каждая следующая попытка ждёт вдвое дольше.
    /// </summary>
    internal TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <inheritdoc />
    public async Task<OrderPage> GetOrdersAsync(
        OrderQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        AbcpApiOptions options = await GetOptionsAsync(cancellationToken).ConfigureAwait(false);
        AbcpQuery parameters = BuildOrdersQuery(query, options);

        if (query.Format is OrderQueryFormat.Paged)
        {
            PagedOrdersDto page = await SendAsync<PagedOrdersDto>(
                OrdersOperation,
                parameters,
                options,
                cancellationToken).ConfigureAwait(false);

            return new OrderPage(page.Items, page.Count);
        }

        List<OrderDto> orders = await SendAsync<List<OrderDto>>(
            OrdersOperation,
            parameters,
            options,
            cancellationToken).ConfigureAwait(false);

        return new OrderPage(orders, orders.Count);
    }

    /// <inheritdoc />
    public async Task<int> GetOrdersCountAsync(
        OrderQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        AbcpApiOptions options = await GetOptionsAsync(cancellationToken).ConfigureAwait(false);

        // format=p отдаёт count вместе с первой страницей; limit=1 экономит трафик.
        OrderQuery countQuery = Clone(query);
        countQuery.Format = OrderQueryFormat.Paged;
        countQuery.Limit = 1;
        countQuery.Skip = 0;

        PagedOrdersDto page = await SendAsync<PagedOrdersDto>(
            OrdersOperation,
            BuildOrdersQuery(countQuery, options),
            options,
            cancellationToken).ConfigureAwait(false);

        return page.Count;
    }

    /// <inheritdoc />
    public async Task<OrderDto?> GetOrderAsync(
        string number,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        AbcpApiOptions options = await GetOptionsAsync(cancellationToken).ConfigureAwait(false);
        AbcpQuery parameters = CreateAuthenticatedQuery(options).Add("number", number);

        try
        {
            return await SendAsync<OrderDto>(OrderOperation, parameters, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AbcpApiException exception) when (exception.ErrorCode == AbcpErrorCodes.ObjectNotFound)
        {
            _logger.LogInformation("Заказ {Number} не найден в портале", number);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderStatusDto>> GetStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        AbcpApiOptions options = await GetOptionsAsync(cancellationToken).ConfigureAwait(false);

        return await SendAsync<List<OrderStatusDto>>(
            StatusesOperation,
            CreateAuthenticatedQuery(options),
            options,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, IReadOnlyList<PositionStatusHistoryDto>>> GetStatusHistoryAsync(
        IReadOnlyCollection<long> positionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positionIds);

        Dictionary<long, IReadOnlyList<PositionStatusHistoryDto>> result = [];
        if (positionIds.Count == 0)
        {
            return result;
        }

        AbcpApiOptions options = await GetOptionsAsync(cancellationToken).ConfigureAwait(false);

        foreach (long[] batch in positionIds.Distinct().Chunk(StatusHistoryBatchSize))
        {
            AbcpQuery parameters = CreateAuthenticatedQuery(options)
                .AddArray("positionsId", batch);

            BatchStatusHistoryDto response = await SendAsync<BatchStatusHistoryDto>(
                StatusHistoryOperation,
                parameters,
                options,
                cancellationToken).ConfigureAwait(false);

            foreach ((string key, List<PositionStatusHistoryDto> entries) in response.Positions)
            {
                if (long.TryParse(key, out long positionId))
                {
                    result[positionId] = entries;
                    continue;
                }

                // Форма узла positions в документации не описана: если ключ не является
                // идентификатором позиции, берём его из самих записей.
                foreach (IGrouping<long, PositionStatusHistoryDto> group in entries
                    .Where(entry => entry.Id.HasValue)
                    .GroupBy(entry => entry.Id!.Value))
                {
                    result[group.Key] = group.ToList();
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArticleInfoDto>> GetArticlesInfoAsync(
        IReadOnlyCollection<ArticleRef> articles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(articles);

        if (articles.Count == 0)
        {
            return [];
        }

        AbcpApiOptions options = await GetOptionsAsync(cancellationToken).ConfigureAwait(false);

        // Уникальные пары «бренд + номер»: в заказе один и тот же артикул
        // может встречаться в нескольких позициях.
        ArticleRef[] unique = articles
            .GroupBy(article => article.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        if (_batchArticlesSupported)
        {
            try
            {
                return await GetArticlesInfoBatchAsync(unique, options, cancellationToken).ConfigureAwait(false);
            }
            catch (AbcpApiException exception) when (exception.ErrorCode == AbcpErrorCodes.UnknownOperation)
            {
                // Пакетная операция есть не на каждом сайте: часть площадок отвечает
                // «Unknown operation». Тогда переходим на одиночные карточки
                // и больше не пробуем пакетную в этом сеансе.
                _batchArticlesSupported = false;

                _logger.LogInformation(
                    "Операция {Operation} недоступна на этом сайте, карточки товаров будут запрашиваться по одной",
                    ArticlesInfoBatchOperation);
            }
        }

        return await GetArticlesInfoOneByOneAsync(unique, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Пакетное получение карточек: один запрос на 100 деталей.
    /// </summary>
    private async Task<IReadOnlyList<ArticleInfoDto>> GetArticlesInfoBatchAsync(
        ArticleRef[] unique,
        AbcpApiOptions options,
        CancellationToken cancellationToken)
    {
        List<ArticleInfoDto> result = [];

        foreach (ArticleRef[] batch in unique.Chunk(ArticlesBatchSize))
        {
            AbcpQuery parameters = CreateAuthenticatedQuery(options);

            for (int index = 0; index < batch.Length; index++)
            {
                parameters.Add($"articles[{index}][brand]", batch[index].Brand);
                parameters.Add($"articles[{index}][number]", batch[index].Number);
            }

            List<ArticleInfoDto> page = await SendAsync<List<ArticleInfoDto>>(
                ArticlesInfoBatchOperation,
                parameters,
                options,
                HttpMethod.Post,
                cancellationToken).ConfigureAwait(false);

            result.AddRange(page);
        }

        return result;
    }

    /// <summary>
    /// Получение карточек по одной операцией <c>articles/info</c>.
    /// </summary>
    /// <remarks>
    /// Формат <c>bnpi</c> просит бренд, номер, свойства и изображения.
    /// Номер в ответе приходит «очищенным», поэтому исходный номер запроса
    /// сохраняется в <see cref="ArticleInfoDto.OuterNumber"/> — по нему карточка
    /// сопоставляется с позицией заказа.
    /// </remarks>
    private async Task<IReadOnlyList<ArticleInfoDto>> GetArticlesInfoOneByOneAsync(
        ArticleRef[] unique,
        AbcpApiOptions options,
        CancellationToken cancellationToken)
    {
        List<ArticleInfoDto> result = [];

        foreach (ArticleRef article in unique)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AbcpQuery parameters = CreateAuthenticatedQuery(options)
                .Add("brand", article.Brand)
                .Add("number", article.Number)
                .Add("format", "bnpi");

            try
            {
                ArticleInfoDto card = await SendAsync<ArticleInfoDto>(
                    ArticleInfoOperation,
                    parameters,
                    options,
                    cancellationToken).ConfigureAwait(false);

                card.OuterNumber ??= article.Number;
                result.Add(card);
            }
            catch (AbcpApiException exception) when (exception.ErrorCode == AbcpErrorCodes.ObjectNotFound)
            {
                // Карточки может не быть — это не повод прерывать остальные позиции.
                _logger.LogDebug(
                    "Карточка товара не найдена: {Brand} {Number}",
                    article.Brand,
                    article.Number);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<ConnectionCheckResult> CheckConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Справочник статусов — самая дешёвая операция, требующая прав API-администратора.
            IReadOnlyList<OrderStatusDto> statuses = await GetStatusesAsync(cancellationToken)
                .ConfigureAwait(false);

            return new ConnectionCheckResult(
                true,
                $"Подключение работает, статусов в справочнике: {statuses.Count}");
        }
        catch (AbcpApiNotConfiguredException exception)
        {
            return new ConnectionCheckResult(false, exception.Message);
        }
        catch (AbcpApiException exception)
        {
            string message = exception.IsAuthenticationFailure
                ? "Неверные реквизиты доступа или у пользователя нет статуса «API-администратор»."
                : exception.Message;

            return new ConnectionCheckResult(false, message, exception.ErrorCode);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new ConnectionCheckResult(false, $"API недоступно: {exception.Message}");
        }
    }

    /// <summary>
    /// Выполняет GET-запрос: параметры уходят строкой запроса.
    /// </summary>
    private Task<T> SendAsync<T>(
        string operation,
        AbcpQuery parameters,
        AbcpApiOptions options,
        CancellationToken cancellationToken) =>
        SendAsync<T>(operation, parameters, options, HttpMethod.Get, cancellationToken);

    /// <summary>
    /// Выполняет запрос с повторами и разбирает ответ.
    /// </summary>
    /// <remarks>
    /// Для POST параметры уходят телом с типом <c>application/x-www-form-urlencoded</c>,
    /// как требует документация API; в строке запроса при этом не остаётся ничего,
    /// включая md5-хэш пароля.
    /// </remarks>
    private async Task<T> SendAsync<T>(
        string operation,
        AbcpQuery parameters,
        AbcpApiOptions options,
        HttpMethod method,
        CancellationToken cancellationToken)
    {
        bool isPost = method == HttpMethod.Post;

        string requestUri = isPost
            ? $"{options.BaseUrl}/{operation}"
            : $"{options.BaseUrl}/{operation}?{parameters.ToQueryString()}";

        string safeUri = isPost
            ? $"{options.BaseUrl}/{operation} [POST {parameters.ToQueryString(maskSecrets: true)}]"
            : $"{options.BaseUrl}/{operation}?{parameters.ToQueryString(maskSecrets: true)}";

        int attempts = Math.Max(1, options.RetryCount + 1);

        for (int attempt = 1; ; attempt++)
        {
            using CancellationTokenSource timeout = CreateTimeoutSource(options, cancellationToken);
            long startedAt = Stopwatch.GetTimestamp();

            try
            {
                using HttpRequestMessage request = new(method, requestUri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if (isPost)
                {
                    request.Content = new StringContent(
                        parameters.ToQueryString(),
                        Encoding.UTF8,
                        "application/x-www-form-urlencoded");
                }

                using HttpResponseMessage response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);

                string body = await response.Content
                    .ReadAsStringAsync(timeout.Token)
                    .ConfigureAwait(false);

                TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);

                // В журнал попадает URL с маскированным userpsw.
                _logger.LogDebug(
                    "API {Operation}: {StatusCode} за {ElapsedMs} мс, попытка {Attempt}. {Uri}",
                    operation,
                    (int)response.StatusCode,
                    (int)elapsed.TotalMilliseconds,
                    attempt,
                    safeUri);

                if (!response.IsSuccessStatusCode)
                {
                    throw CreateApiException(operation, response.StatusCode, body);
                }

                return Deserialize<T>(operation, body);
            }
            catch (AbcpApiException exception) when (ShouldRetry(exception) && attempt < attempts)
            {
                await DelayBeforeRetryAsync(operation, attempt, exception.Message, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (attempt < attempts)
            {
                await DelayBeforeRetryAsync(operation, attempt, exception.Message, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Отмена по таймауту запроса, а не по требованию вызывающего кода.
                if (attempt >= attempts)
                {
                    throw new AbcpApiException(
                        $"Превышен таймаут запроса ({options.TimeoutSeconds} с).",
                        operation: operation);
                }

                await DelayBeforeRetryAsync(operation, attempt, "таймаут запроса", cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Повторять стоит только временные сбои: ответы 5xx, 429 и ошибки кэша/блокировки.
    /// </summary>
    private static bool ShouldRetry(AbcpApiException exception)
    {
        if (exception.IsPermanent)
        {
            return false;
        }

        // Ошибка 303 означает исчерпанный лимит запросов: повторы только продлевают
        // блокировку, поэтому вызывающий код должен сам сделать паузу.
        if (exception.ErrorCode == AbcpErrorCodes.ResourceLocked)
        {
            return false;
        }

        if (exception.ErrorCode == AbcpErrorCodes.CacheError)
        {
            return true;
        }

        return exception.StatusCode is { } status
            && ((int)status >= 500 || status == HttpStatusCode.TooManyRequests);
    }

    private async Task DelayBeforeRetryAsync(
        string operation,
        int attempt,
        string reason,
        CancellationToken cancellationToken)
    {
        TimeSpan delay = RetryBaseDelay * Math.Pow(2, attempt - 1);

        _logger.LogWarning(
            "Повтор запроса {Operation} через {DelayMs} мс после попытки {Attempt}: {Reason}",
            operation,
            (int)delay.TotalMilliseconds,
            attempt,
            reason);

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static T Deserialize<T>(string operation, string body)
    {
        try
        {
            T? value = JsonSerializer.Deserialize<T>(body, AbcpJson.Options);
            if (value is null)
            {
                throw new AbcpApiException("Пустой ответ API.", operation: operation);
            }

            return value;
        }
        catch (JsonException exception)
        {
            throw new AbcpApiException(
                $"Не удалось разобрать ответ API: {exception.Message}",
                operation: operation,
                innerException: exception);
        }
    }

    private static AbcpApiException CreateApiException(
        string operation,
        HttpStatusCode statusCode,
        string body)
    {
        ApiErrorDto? error = null;
        try
        {
            error = JsonSerializer.Deserialize<ApiErrorDto>(body, AbcpJson.Options);
        }
        catch (JsonException)
        {
            // Тело не является JSON: сообщение соберём из HTTP-кода.
        }

        string message = error?.ErrorMessage is { Length: > 0 } text
            ? $"API вернуло ошибку {error.ErrorCode}: {text}"
            : $"API вернуло HTTP {(int)statusCode}.";

        return new AbcpApiException(message, statusCode, error?.ErrorCode, operation);
    }

    private async Task<AbcpApiOptions> GetOptionsAsync(CancellationToken cancellationToken)
    {
        AbcpApiOptions options = await _settings
            .GetApiOptionsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!options.IsConfigured)
        {
            throw new AbcpApiNotConfiguredException();
        }

        return options;
    }

    private static AbcpQuery CreateAuthenticatedQuery(AbcpApiOptions options) =>
        new AbcpQuery()
            .Add("userlogin", options.Login)
            .Add("userpsw", options.PasswordMd5);

    private static AbcpQuery BuildOrdersQuery(OrderQuery query, AbcpApiOptions options)
    {
        AbcpQuery parameters = CreateAuthenticatedQuery(options)
            .Add("dateCreatedStart", query.DateCreatedStart)
            .Add("dateCreatedEnd", query.DateCreatedEnd)
            .Add("dateUpdatedStart", query.DateUpdatedStart)
            .Add("dateUpdatedEnd", query.DateUpdatedEnd)
            .Add("userId", query.UserId)
            .Add("officeId", query.OfficeId)
            .Add("withDeleted", query.WithDeleted)
            .Add("isArchive", query.IsArchive)
            .Add("desc", query.Descending)
            .Add("skip", query.Skip)
            .Add("limit", Math.Min(query.Limit ?? options.PageSize, AbcpApiOptions.MaxPageSize));

        parameters.AddArray("numbers", query.Numbers);

        // internalNumbers учитывается API только при отсутствии numbers.
        if (query.Numbers is null || query.Numbers.Count == 0)
        {
            parameters.AddArray("internalNumbers", query.InternalNumbers);
        }

        parameters.AddArray("statusCode", query.StatusCodes);

        string? format = query.Format switch
        {
            OrderQueryFormat.Paged => "p",
            OrderQueryFormat.Short => "short",
            OrderQueryFormat.StatusOnly => "status_only",
            OrderQueryFormat.CountOnly => "count",
            OrderQueryFormat.Additional => "additional",
            _ => null,
        };

        return parameters.Add("format", format);
    }

    private static CancellationTokenSource CreateTimeoutSource(
        AbcpApiOptions options,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.TimeoutSeconds > 0)
        {
            source.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        }

        return source;
    }

    private static OrderQuery Clone(OrderQuery query) => new()
    {
        DateCreatedStart = query.DateCreatedStart,
        DateCreatedEnd = query.DateCreatedEnd,
        DateUpdatedStart = query.DateUpdatedStart,
        DateUpdatedEnd = query.DateUpdatedEnd,
        Numbers = query.Numbers,
        InternalNumbers = query.InternalNumbers,
        StatusCodes = query.StatusCodes,
        UserId = query.UserId,
        OfficeId = query.OfficeId,
        WithDeleted = query.WithDeleted,
        IsArchive = query.IsArchive,
        Descending = query.Descending,
        Skip = query.Skip,
        Limit = query.Limit,
        Format = query.Format,
    };
}
