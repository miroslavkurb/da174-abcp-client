using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ABCPClient.Contracts;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Mobile.Services;

/// <summary>
/// Обращения к узлу склада — программе на компьютере.
/// </summary>
/// <remarks>
/// Адрес узла и токен устройства хранятся в защищённом хранилище: токен даёт
/// доступ к заданиям склада, и лежать в обычных настройках ему незачем.
/// Ошибки сети возвращаются как <see cref="HubResult{T}"/> с текстом, а не
/// исключениями: на складе связь пропадает регулярно, и это обычный ход работы,
/// а не сбой программы.
/// </remarks>
public sealed class HubClient
{
    /// <summary>Имя клиента <c>IHttpClientFactory</c> для узла склада.</summary>
    public const string HttpClientName = "warehouse-hub";

    private const string AddressKey = "ABCPClient.Hub.Address";
    private const string TokenKey = "ABCPClient.Hub.Token";
    private const string DeviceKey = "ABCPClient.Hub.Device";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HubClient> _logger;

    /// <summary>Создаёт клиент узла.</summary>
    public HubClient(IHttpClientFactory httpClientFactory, ILogger<HubClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Адрес узла, например <c>http://192.168.0.103:5080</c>.</summary>
    public string? Address { get; private set; }

    /// <summary>Имя этого устройства на узле.</summary>
    public string? DeviceName { get; private set; }

    /// <summary>Устройство подключено к узлу.</summary>
    public bool IsPaired => !string.IsNullOrWhiteSpace(Address) && !string.IsNullOrWhiteSpace(_token);

    private string? _token;

    /// <summary>
    /// Читает сохранённые адрес и токен.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            Address = await SecureStorage.GetAsync(AddressKey).ConfigureAwait(false);
            _token = await SecureStorage.GetAsync(TokenKey).ConfigureAwait(false);
            DeviceName = await SecureStorage.GetAsync(DeviceKey).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось прочитать данные подключения к узлу");
        }
    }

    /// <summary>
    /// Проверяет, отвечает ли узел по указанному адресу.
    /// </summary>
    /// <remarks>
    /// Отдельный шаг до подключения: если адрес набран неверно, сборщик должен
    /// узнать это до того, как вводить код.
    /// </remarks>
    /// <param name="address">Адрес узла.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<HubResult<bool>> CheckAsync(string address, CancellationToken cancellationToken = default)
    {
        if (!TryNormalize(address, out string? normalized))
        {
            return HubResult<bool>.Failure("Адрес должен быть вида http://192.168.0.103:5080");
        }

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

            using HttpResponseMessage response = await client
                .GetAsync(normalized + "/api/health", cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? HubResult<bool>.Success(true)
                : HubResult<bool>.Failure($"Узел ответил {(int)response.StatusCode}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return HubResult<bool>.Failure(Explain(exception));
        }
    }

    /// <summary>
    /// Подключает устройство к узлу по коду сопряжения.
    /// </summary>
    /// <param name="address">Адрес узла.</param>
    /// <param name="pairingCode">Код, показанный в программе на компьютере.</param>
    /// <param name="deviceName">Имя устройства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<HubResult<string>> PairAsync(
        string address,
        string pairingCode,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalize(address, out string? normalized))
        {
            return HubResult<string>.Failure("Адрес должен быть вида http://192.168.0.103:5080");
        }

        if (string.IsNullOrWhiteSpace(pairingCode))
        {
            return HubResult<string>.Failure("Введите код, показанный в программе на компьютере");
        }

        string name = string.IsNullOrWhiteSpace(deviceName) ? DeviceInfo.Current.Name : deviceName.Trim();

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

            using HttpResponseMessage response = await client
                .PostAsJsonAsync(
                    normalized + "/api/devices/pair",
                    new DeviceAuthRequest(pairingCode.Trim(), name),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return HubResult<string>.Failure(await ReadErrorAsync(response, cancellationToken)
                    .ConfigureAwait(false));
            }

            DeviceAuthResponse? auth = await response.Content
                .ReadFromJsonAsync<DeviceAuthResponse>(cancellationToken)
                .ConfigureAwait(false);

            if (auth is null)
            {
                return HubResult<string>.Failure("Узел вернул неожиданный ответ");
            }

            Address = normalized;
            _token = auth.Token;
            DeviceName = auth.DeviceName;

            await SecureStorage.SetAsync(AddressKey, normalized).ConfigureAwait(false);
            await SecureStorage.SetAsync(TokenKey, auth.Token).ConfigureAwait(false);
            await SecureStorage.SetAsync(DeviceKey, auth.DeviceName).ConfigureAwait(false);

            _logger.LogInformation("Устройство подключено к узлу {Address} как «{Device}»", normalized, auth.DeviceName);

            return HubResult<string>.Success(auth.DeviceName);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return HubResult<string>.Failure(Explain(exception));
        }
    }

    /// <summary>Забывает подключение к узлу.</summary>
    public async Task ForgetAsync()
    {
        Address = null;
        _token = null;
        DeviceName = null;

        SecureStorage.Remove(AddressKey);
        SecureStorage.Remove(TokenKey);
        SecureStorage.Remove(DeviceKey);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Возвращает задания на сборку.</summary>
    /// <param name="onlyOpen">Только незакрытые.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task<HubResult<PickingTaskSummary[]>> GetTasksAsync(
        bool onlyOpen = true,
        CancellationToken cancellationToken = default) =>
        GetAsync<PickingTaskSummary[]>(
            $"/api/picking/tasks?onlyOpen={(onlyOpen ? "true" : "false")}",
            cancellationToken);

    /// <summary>Возвращает задание со строками.</summary>
    /// <param name="id">Идентификатор задания.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task<HubResult<PickingTaskDetails>> GetTaskAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        GetAsync<PickingTaskDetails>($"/api/picking/tasks/{id}", cancellationToken);

    /// <summary>Фиксирует собранное количество.</summary>
    /// <param name="taskId">Задание.</param>
    /// <param name="lineId">Строка.</param>
    /// <param name="quantity">Собранное количество.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task<HubResult<PickingTaskDetails>> PickAsync(
        int taskId,
        int lineId,
        decimal quantity,
        CancellationToken cancellationToken = default) =>
        PostAsync<PickLineRequest, PickingTaskDetails>(
            $"/api/picking/tasks/{taskId}/lines/{lineId}/pick",
            new PickLineRequest(quantity),
            cancellationToken);

    /// <summary>Закрывает задание.</summary>
    /// <param name="taskId">Задание.</param>
    /// <param name="comment">Комментарий сборщика.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task<HubResult<PickingTaskDetails>> CompleteAsync(
        int taskId,
        string? comment = null,
        CancellationToken cancellationToken = default) =>
        PostAsync<CompleteTaskRequest, PickingTaskDetails>(
            $"/api/picking/tasks/{taskId}/complete",
            new CompleteTaskRequest(comment),
            cancellationToken);

    /// <summary>Ищет деталь по штрихкоду или артикулу на узле.</summary>
    /// <param name="query">Строка поиска.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task<HubResult<ArticleLookupResponse>> LookupAsync(
        string query,
        CancellationToken cancellationToken = default) =>
        GetAsync<ArticleLookupResponse>(
            "/api/articles/lookup?query=" + Uri.EscapeDataString(query),
            cancellationToken);

    /// <summary>
    /// Скачивает изображение товара с узла.
    /// </summary>
    /// <remarks>
    /// Изображения отдаёт узел из своего кэша: телефон не обращается ни к API,
    /// ни к сторонним хостам — на складе интернета может не быть.
    /// Загрузка идёт в память, а не привязкой к адресу: узел требует токен и на
    /// изображения, а элемент разметки заголовков не отправляет.
    /// </remarks>
    /// <param name="imageName">Имя или адрес изображения из строки задания.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<byte[]?> GetImageAsync(string? imageName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageName) || !IsPaired)
        {
            return null;
        }

        try
        {
            HttpClient client = CreateAuthenticatedClient();

            using HttpResponseMessage response = await client
                .GetAsync(
                    $"{Address}/api/images?name={Uri.EscapeDataString(imageName)}",
                    cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Картинка — не то, из-за чего стоит прерывать сборку.
            _logger.LogDebug(exception, "Изображение {Image} с узла не получено", imageName);
            return null;
        }
    }

    private async Task<HubResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!IsPaired)
        {
            return HubResult<T>.Failure("Устройство не подключено к узлу");
        }

        try
        {
            HttpClient client = CreateAuthenticatedClient();

            using HttpResponseMessage response = await client
                .GetAsync(Address + path, cancellationToken)
                .ConfigureAwait(false);

            return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return HubResult<T>.Failure(Explain(exception));
        }
    }

    private async Task<HubResult<TResponse>> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken cancellationToken)
    {
        if (!IsPaired)
        {
            return HubResult<TResponse>.Failure("Устройство не подключено к узлу");
        }

        try
        {
            HttpClient client = CreateAuthenticatedClient();

            using HttpResponseMessage response = await client
                .PostAsJsonAsync(Address + path, body, cancellationToken)
                .ConfigureAwait(false);

            return await ReadAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return HubResult<TResponse>.Failure(Explain(exception));
        }
    }

    private HttpClient CreateAuthenticatedClient()
    {
        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        return client;
    }

    private async Task<HubResult<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            string message = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);

            // Токен отозвали в программе на компьютере — устройству нужно
            // подключиться заново, и сказать об этом надо прямо.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return HubResult<T>.Failure(message, requiresPairing: true);
            }

            return HubResult<T>.Failure(message);
        }

        T? value = await response.Content
            .ReadFromJsonAsync<T>(cancellationToken)
            .ConfigureAwait(false);

        return value is null
            ? HubResult<T>.Failure("Узел вернул пустой ответ")
            : HubResult<T>.Success(value);
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            HubError? error = await response.Content
                .ReadFromJsonAsync<HubError>(cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(error?.Error))
            {
                return error.Error;
            }
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or HttpRequestException)
        {
            // Ответ не в нашем формате — покажем хотя бы код.
        }

        return $"Узел ответил {(int)response.StatusCode}";
    }

    /// <summary>
    /// Приводит адрес к виду без завершающей косой черты, подставляя схему.
    /// </summary>
    internal static bool TryNormalize(string? address, out string? normalized)
    {
        normalized = null;

        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        string text = address.Trim().TrimEnd('/');

        // Схему набирать на телефоне неудобно, поэтому она подставляется сама.
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        normalized = text;
        return true;
    }

    private static string Explain(Exception exception) => exception switch
    {
        TaskCanceledException => "Узел не ответил вовремя. Проверьте, что программа на компьютере запущена "
            + "и устройство в той же сети",
        _ => "Нет связи с узлом. Проверьте адрес, сеть склада и брандмауэр на компьютере",
    };
}

/// <summary>
/// Итог обращения к узлу.
/// </summary>
/// <typeparam name="T">Тип значения.</typeparam>
/// <param name="Value">Значение или <c>null</c> при ошибке.</param>
/// <param name="Error">Текст ошибки или <c>null</c> при успехе.</param>
/// <param name="RequiresPairing">Нужно подключиться к узлу заново.</param>
public sealed record HubResult<T>(T? Value, string? Error, bool RequiresPairing = false)
{
    /// <summary>Обращение удалось.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>Создаёт успешный итог.</summary>
    public static HubResult<T> Success(T value) => new(value, null);

    /// <summary>Создаёт неуспешный итог.</summary>
    public static HubResult<T> Failure(string error, bool requiresPairing = false) =>
        new(default, error, requiresPairing);
}
