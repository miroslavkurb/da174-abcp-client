using System.Text.RegularExpressions;
using System.Web;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Infrastructure.Integration;

/// <summary>
/// Карточки деталей со страниц товара на витрине магазина.
/// </summary>
/// <remarks>
/// Выгрузка каталога покрывает только собственное наличие, а заказы почти целиком
/// состоят из деталей под заказ: на живых данных из 343 артикулов в заказах в каталоге
/// нашлись 53. Страница товара на витрине есть у всего, чем магазин торгует, и витрина —
/// обычный сайт, а не API: лимит вызовов API её запросы не расходуют.
/// Адрес страницы — <c>{витрина}/parts/{бренд}/{номер}</c>; изображение и наименование
/// берутся из разметки Open Graph, которую платформа выводит на каждой такой странице.
/// </remarks>
public sealed partial class StorefrontArticleSource : IStorefrontArticleSource
{
    /// <summary>Имя клиента <c>IHttpClientFactory</c> для витрины.</summary>
    public const string HttpClientName = "abcp-storefront";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAbcpSettingsProvider _settings;
    private readonly ILogger<StorefrontArticleSource> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTimeOffset> _requestTimes = new();

    /// <summary>Создаёт источник.</summary>
    public StorefrontArticleSource(
        IHttpClientFactory httpClientFactory,
        IAbcpSettingsProvider settings,
        ILogger<StorefrontArticleSource> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Источник времени. Отдельным свойством — ради предсказуемости тестов.</summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Пауза между обращениями к витрине.</summary>
    internal TimeSpan RequestSpacing { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        CatalogOptions options = await _settings.GetCatalogOptionsAsync(cancellationToken).ConfigureAwait(false);
        return TryGetBaseUri(options.StorefrontUrl, out _);
    }

    /// <inheritdoc />
    public async Task<StorefrontArticle?> GetAsync(
        ArticleRef article,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(article);

        CatalogOptions options = await _settings.GetCatalogOptionsAsync(cancellationToken).ConfigureAwait(false);
        if (!TryGetBaseUri(options.StorefrontUrl, out Uri? baseUri) || baseUri is null)
        {
            return null;
        }

        // Бренд и номер попадают в путь адреса: сегменты экранируются,
        // чтобы пробел в «ACS Termal» и косая черта в номере не ломали ссылку.
        Uri address = new(
            baseUri,
            $"parts/{Uri.EscapeDataString(article.Brand.Trim())}/{Uri.EscapeDataString(article.Number.Trim())}");

        if (!await TryReserveSlotAsync(options, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

            using HttpResponseMessage response = await client
                .GetAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Витрина не отдала страницу {Brand} {Number}: HTTP {StatusCode}",
                    article.Brand,
                    article.Number,
                    (int)response.StatusCode);

                return null;
            }

            string page = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return Parse(page, article);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(
                exception,
                "Не удалось прочитать страницу витрины для {Brand} {Number}",
                article.Brand,
                article.Number);

            return null;
        }
    }

    /// <summary>
    /// Достаёт изображение и наименование из разметки Open Graph.
    /// </summary>
    /// <remarks>
    /// У несуществующего товара страница отдаётся с кодом 200, но <c>og:title</c>
    /// и <c>og:image</c> пустые — это и есть признак «витрина такого не знает».
    /// </remarks>
    internal static StorefrontArticle Parse(string page, ArticleRef article)
    {
        string? image = Meta(page, "og:image");
        string? title = Meta(page, "og:title") ?? Meta(page, "og:description");

        if (image is not null
            && (!Uri.TryCreate(image, UriKind.Absolute, out Uri? imageUri)
                || (imageUri.Scheme != Uri.UriSchemeHttp && imageUri.Scheme != Uri.UriSchemeHttps)))
        {
            image = null;
        }

        return new StorefrontArticle(image, StripPrefix(title, article));
    }

    private static string? Meta(string page, string property)
    {
        Match match = MetaPattern().Match(page);

        while (match.Success)
        {
            if (string.Equals(match.Groups["name"].Value, property, StringComparison.OrdinalIgnoreCase))
            {
                string value = HttpUtility.HtmlDecode(match.Groups["value"].Value).Trim();
                return value.Length == 0 ? null : value;
            }

            match = match.NextMatch();
        }

        return null;
    }

    /// <summary>
    /// Убирает из наименования ведущие бренд и артикул: платформа выводит
    /// заголовок вида «Bosch 0258006537 Датчик кислорода».
    /// </summary>
    private static string? StripPrefix(string? title, ArticleRef article)
    {
        if (title is null)
        {
            return null;
        }

        string result = title;

        foreach (string prefix in new[] { article.Brand.Trim(), article.Number.Trim() })
        {
            if (prefix.Length > 0 && result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                result = result[prefix.Length..].TrimStart();
            }
        }

        return result.Length == 0 ? title : result;
    }

    private static bool TryGetBaseUri(string? value, out Uri? baseUri)
    {
        baseUri = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Завершающая косая черта обязательна: без неё последний сегмент
        // адреса заменяется, а не дополняется.
        string normalized = value.Trim().TrimEnd('/') + "/";

        return Uri.TryCreate(normalized, UriKind.Absolute, out baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Ограничивает частоту обращений к витрине скользящим окном в минуту.
    /// </summary>
    /// <remarks>
    /// Витрина — рабочий сайт магазина, и сотни запросов подряд ей ни к чему.
    /// </remarks>
    private async Task<bool> TryReserveSlotAsync(CatalogOptions options, CancellationToken cancellationToken)
    {
        int perMinute = Math.Clamp(options.StorefrontRequestsPerMinute, 1, 600);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = Time.GetUtcNow();

            while (_requestTimes.Count > 0 && now - _requestTimes.Peek() >= TimeSpan.FromMinutes(1))
            {
                _requestTimes.Dequeue();
            }

            if (_requestTimes.Count >= perMinute)
            {
                return false;
            }

            if (_requestTimes.Count > 0 && RequestSpacing > TimeSpan.Zero)
            {
                await Task.Delay(RequestSpacing, Time, cancellationToken).ConfigureAwait(false);
            }

            _requestTimes.Enqueue(Time.GetUtcNow());
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    [GeneratedRegex(
        """<meta\s+[^>]*?property\s*=\s*["'](?<name>og:[a-z:]+)["'][^>]*?content\s*=\s*["'](?<value>[^"']*)["']""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaPattern();
}
