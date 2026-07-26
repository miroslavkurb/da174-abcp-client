using System.Security.Cryptography;
using System.Text;
using ABCPClient.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Infrastructure.Api;

/// <summary>
/// Кэш изображений товаров в каталоге данных приложения.
/// </summary>
/// <remarks>
/// Изображения лежат на CDN платформы и доступны без авторизации, поэтому загрузка
/// идёт отдельным <see cref="HttpClient"/> без реквизитов API: незачем отправлять
/// логин и хэш пароля на сторонний хост. Обращения к CDN не расходуют лимит вызовов
/// API — ограничен только сам API.
/// Источник задаётся двумя способами: API отдаёт имя файла на CDN платформы,
/// а выгрузка каталога магазина — полный адрес изображения на другом хосте.
/// </remarks>
public sealed class ProductImageCache : IProductImageCache
{
    /// <summary>Имя клиента <c>IHttpClientFactory</c> для загрузки изображений.</summary>
    public const string HttpClientName = "abcp-images";

    /// <summary>Базовый адрес изображений деталей.</summary>
    public const string ImageBaseUrl = "https://imgcdn.abcp.ru/p/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProductImageCache> _logger;
    private readonly string _directory;

    /// <summary>
    /// Параллельные запросы одной и той же картинки должны скачать её один раз:
    /// в карточке заказа несколько позиций легко ссылаются на одно изображение.
    /// </summary>
    private readonly Dictionary<string, Task<string?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Создаёт кэш изображений.</summary>
    public ProductImageCache(IHttpClientFactory httpClientFactory, ILogger<ProductImageCache> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _directory = Path.Combine(AppPaths.DataDirectory, "images");
        Directory.CreateDirectory(_directory);
    }

    /// <inheritdoc />
    public Task<string?> GetOrDownloadAsync(string imageName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageName))
        {
            return Task.FromResult<string?>(null);
        }

        if (!TryResolve(imageName.Trim(), out string address, out string safeName))
        {
            return Task.FromResult<string?>(null);
        }

        string path = Path.Combine(_directory, safeName);
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return Task.FromResult<string?>(path);
        }

        lock (_inFlight)
        {
            if (_inFlight.TryGetValue(safeName, out Task<string?>? pending))
            {
                return pending;
            }

            Task<string?> download = DownloadAsync(address, safeName, path, cancellationToken);
            _inFlight[safeName] = download;

            return download;
        }
    }

    /// <summary>
    /// Определяет адрес загрузки и имя файла в кэше.
    /// </summary>
    /// <remarks>
    /// Полный адрес приходит из выгрузки каталога и может указывать на любой хост,
    /// поэтому имя файла берётся не из адреса, а из его хэша: так исключены
    /// и совпадения имён между хостами, и выход за пределы каталога кэша.
    /// </remarks>
    internal static bool TryResolve(string imageName, out string address, out string fileName)
    {
        if (Uri.TryCreate(imageName, UriKind.Absolute, out Uri? uri))
        {
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                address = string.Empty;
                fileName = string.Empty;
                return false;
            }

            string extension = Path.GetExtension(uri.AbsolutePath);
            if (extension.Length is 0 or > 8 || extension.Any(symbol => !char.IsLetterOrDigit(symbol) && symbol != '.'))
            {
                extension = ".img";
            }

            address = uri.AbsoluteUri;
            fileName = Hash(uri.AbsoluteUri) + extension.ToLowerInvariant();
            return true;
        }

        // Имя из ответа API: берём только имя файла, чтобы «../» в значении
        // не увёл запись за пределы каталога кэша.
        fileName = Path.GetFileName(imageName);
        if (fileName.Length == 0)
        {
            address = string.Empty;
            return false;
        }

        address = ImageBaseUrl + Uri.EscapeDataString(fileName);
        return true;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();

    private async Task<string?> DownloadAsync(
        string address,
        string safeName,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

            using HttpResponseMessage response = await client
                .GetAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Изображение {Image} недоступно: HTTP {StatusCode}",
                    safeName,
                    (int)response.StatusCode);
                return null;
            }

            byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (content.Length == 0)
            {
                return null;
            }

            // Сначала во временный файл, затем перемещение: иначе при обрыве загрузки
            // в кэше остался бы обрезанный файл, который считался бы готовым.
            string temporary = path + ".part";
            await File.WriteAllBytesAsync(temporary, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);

            return path;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            _logger.LogDebug(exception, "Не удалось загрузить изображение {Image}", safeName);
            return null;
        }
        finally
        {
            lock (_inFlight)
            {
                _inFlight.Remove(safeName);
            }
        }
    }
}
