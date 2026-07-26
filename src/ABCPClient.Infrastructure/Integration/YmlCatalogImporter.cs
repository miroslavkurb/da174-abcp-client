using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Infrastructure.Integration;

/// <summary>
/// Импорт каталога магазина из выгрузки YML в кэш карточек товаров.
/// </summary>
/// <remarks>
/// Выгрузка формируется самой платформой и содержит по каждому предложению бренд
/// (<c>vendor</c>), артикул (<c>vendorCode</c>), описание, свойства, изображения
/// и штрихкоды — то же, что отдаёт карточка товара, но сразу по всему ассортименту.
/// Поэтому импорт полностью снимает нагрузку с API: обращения к нему остаются
/// только для чужих артикулов, которых в каталоге магазина нет.
/// Файл читается потоком: выгрузка легко занимает десятки мегабайт.
/// </remarks>
public sealed class YmlCatalogImporter : ICatalogImporter
{
    /// <summary>Имя клиента <c>IHttpClientFactory</c> для загрузки выгрузки.</summary>
    public const string HttpClientName = "abcp-catalog";

    /// <summary>Сколько карточек накапливается перед записью в базу.</summary>
    private const int BatchSize = 500;

    /// <summary>Сколько изображений скачивается одновременно.</summary>
    private const int ImageParallelism = 4;

    private readonly IArticleCardRepository _repository;
    private readonly IAbcpSettingsProvider _settings;
    private readonly IAppSettingsStore _store;
    private readonly IProductImageCache _images;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YmlCatalogImporter> _logger;

    /// <summary>Создаёт импортёр каталога.</summary>
    public YmlCatalogImporter(
        IArticleCardRepository repository,
        IAbcpSettingsProvider settings,
        IAppSettingsStore store,
        IProductImageCache images,
        IHttpClientFactory httpClientFactory,
        ILogger<YmlCatalogImporter> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _settings = settings;
        _store = store;
        _images = images;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Источник времени. Отдельным свойством — ради предсказуемости тестов.</summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    /// <inheritdoc />
    public async Task<CatalogImportResult> ImportAsync(
        string? source = null,
        IProgress<CatalogImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CatalogOptions options = await _settings.GetCatalogOptionsAsync(cancellationToken).ConfigureAwait(false);

        string feed = string.IsNullOrWhiteSpace(source) ? options.FeedPath : source.Trim();
        if (string.IsNullOrWhiteSpace(feed))
        {
            throw new InvalidOperationException(
                "Не указан путь к выгрузке каталога. Задайте его в настройках приложения.");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        await using Stream stream = await OpenAsync(feed, cancellationToken).ConfigureAwait(false);

        ImportState state = new();
        List<string> imagesToPrefetch = [];

        XmlReaderSettings readerSettings = new()
        {
            Async = true,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,

            // Выгрузка приходит извне: внешние сущности и DTD не разбираем,
            // иначе файл смог бы обратиться к посторонним ресурсам.
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        using (XmlReader reader = XmlReader.Create(stream, readerSettings))
        {
            Dictionary<string, ArticleCard> batch = new(StringComparer.Ordinal);

            while (!reader.EOF)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.NodeType != XmlNodeType.Element)
                {
                    await reader.ReadAsync().ConfigureAwait(false);
                    continue;
                }

                if (string.Equals(reader.Name, "yml_catalog", StringComparison.Ordinal))
                {
                    state.FeedDate = ParseFeedDate(reader.GetAttribute("date"));
                    await reader.ReadAsync().ConfigureAwait(false);
                    continue;
                }

                if (!string.Equals(reader.Name, "offer", StringComparison.Ordinal))
                {
                    await reader.ReadAsync().ConfigureAwait(false);
                    continue;
                }

                CatalogOffer offer = await ReadOfferAsync(reader, cancellationToken).ConfigureAwait(false);
                state.Offers++;

                if (!TryCreateCard(offer, out ArticleCard? card, out string? key))
                {
                    state.Skipped++;
                    continue;
                }

                if (card.ImageName is not null)
                {
                    state.WithImages++;
                    if (options.PrefetchImages)
                    {
                        imagesToPrefetch.Add(card.ImageName);
                    }
                }

                if (card.Barcodes is not null)
                {
                    state.WithBarcodes++;
                }

                // Один и тот же артикул встречается в выгрузке несколько раз
                // (разные склады и цены): в пределах пачки данные объединяются,
                // между пачками пустые значения не затирают уже сохранённые.
                batch[key] = card;

                if (batch.Count >= BatchSize)
                {
                    await FlushAsync(batch, state, progress, cancellationToken).ConfigureAwait(false);
                }
            }

            await FlushAsync(batch, state, progress, cancellationToken).ConfigureAwait(false);
        }

        int downloaded = imagesToPrefetch.Count == 0
            ? 0
            : await PrefetchImagesAsync(imagesToPrefetch, progress, cancellationToken).ConfigureAwait(false);

        await _store.SetAsync(
                AppSettingKeys.CatalogLastImportAt,
                Time.GetLocalNow().ToString("O", CultureInfo.InvariantCulture),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();

        _logger.LogInformation(
            "Каталог импортирован из {Source}: предложений {Offers}, карточек {Cards}, "
                + "с изображениями {WithImages}, со штрихкодами {WithBarcodes}, пропущено {Skipped}",
            feed,
            state.Offers,
            state.Cards,
            state.WithImages,
            state.WithBarcodes,
            state.Skipped);

        return new CatalogImportResult(
            feed,
            state.FeedDate,
            state.Offers,
            state.Cards,
            state.WithImages,
            state.WithBarcodes,
            state.Skipped,
            downloaded,
            stopwatch.Elapsed);
    }

    /// <summary>
    /// Открывает выгрузку: локальный файл или адрес в сети.
    /// </summary>
    private async Task<Stream> OpenAsync(string feed, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(feed, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

            HttpResponseMessage response = await client
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!File.Exists(feed))
        {
            throw new FileNotFoundException($"Выгрузка каталога не найдена: {feed}", feed);
        }

        return new FileStream(
            feed,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    /// <summary>
    /// Читает содержимое элемента <c>offer</c>, оставляя читатель за его закрывающим тегом.
    /// </summary>
    private static async Task<CatalogOffer> ReadOfferAsync(XmlReader reader, CancellationToken cancellationToken)
    {
        CatalogOffer offer = new();

        if (reader.IsEmptyElement)
        {
            await reader.ReadAsync().ConfigureAwait(false);
            return offer;
        }

        int depth = reader.Depth;
        await reader.ReadAsync().ConfigureAwait(false);

        while (!reader.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                await reader.ReadAsync().ConfigureAwait(false);
                break;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                await reader.ReadAsync().ConfigureAwait(false);
                continue;
            }

            string name = reader.Name;
            string? parameterName = string.Equals(name, "param", StringComparison.Ordinal)
                ? reader.GetAttribute("name")
                : null;

            // Значения читаются только у известных элементов: у остальных может
            // оказаться вложенная структура, на которой чтение текста упало бы.
            if (!IsKnownElement(name))
            {
                reader.Skip();
                continue;
            }

            string value = (await reader.ReadElementContentAsStringAsync().ConfigureAwait(false)).Trim();
            if (value.Length == 0)
            {
                continue;
            }

            switch (name)
            {
                case "vendor":
                    offer.Vendor = value;
                    break;
                case "vendorCode":
                    offer.VendorCode = value;
                    break;
                case "description":
                    offer.Description = value;
                    break;
                case "name":
                    offer.Name = value;
                    break;
                case "picture":
                    offer.Pictures.Add(value);
                    break;
                case "barcode":
                    offer.Barcodes.Add(value);
                    break;
                case "param" when !string.IsNullOrWhiteSpace(parameterName):
                    offer.Parameters[parameterName!] = value;
                    break;
            }
        }

        return offer;
    }

    private static bool IsKnownElement(string name) => name switch
    {
        "vendor" or "vendorCode" or "description" or "name" or "picture" or "barcode" or "param" => true,
        _ => false,
    };

    private bool TryCreateCard(CatalogOffer offer, out ArticleCard card, out string key)
    {
        card = null!;
        key = string.Empty;

        if (string.IsNullOrWhiteSpace(offer.Vendor) || string.IsNullOrWhiteSpace(offer.VendorCode))
        {
            return false;
        }

        ArticleRef reference = new(offer.Vendor, offer.VendorCode);
        key = reference.Key;

        card = new ArticleCard
        {
            Brand = offer.Vendor,
            Number = offer.VendorCode,

            // Описание из выгрузки — без бренда и артикула в хвосте,
            // в отличие от названия предложения.
            Description = Trim(offer.Description ?? offer.Name, 1024),
            ImageName = offer.Pictures.Count > 0 ? Trim(offer.Pictures[0], 512) : null,
            ImagesCount = offer.Pictures.Count,
            PropertiesJson = offer.Parameters.Count == 0
                ? null
                : Trim(JsonSerializer.Serialize(offer.Parameters), 8192),
            Barcodes = offer.Barcodes.Count == 0
                ? null
                : Trim(string.Join(';', offer.Barcodes.Distinct(StringComparer.Ordinal)), 256),
            NotFound = false,
            Source = ArticleCardSource.Catalog,
            SyncedAt = Time.GetLocalNow().DateTime,
        };

        return true;
    }

    private async Task FlushAsync(
        Dictionary<string, ArticleCard> batch,
        ImportState state,
        IProgress<CatalogImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        await _repository.UpsertAsync(batch.Values.ToArray(), cancellationToken).ConfigureAwait(false);

        state.Cards += batch.Count;
        batch.Clear();

        progress?.Report(new CatalogImportProgress("Карточки товаров", state.Cards));
    }

    /// <summary>
    /// Заранее скачивает изображения каталога.
    /// </summary>
    /// <remarks>
    /// Изображения лежат на CDN, а не на API, поэтому массовая загрузка лимит
    /// вызовов API не расходует. Параллелизм ограничен, чтобы не забивать канал.
    /// </remarks>
    private async Task<int> PrefetchImagesAsync(
        IReadOnlyList<string> images,
        IProgress<CatalogImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        int downloaded = 0;
        int processed = 0;

        using SemaphoreSlim gate = new(ImageParallelism, ImageParallelism);

        IEnumerable<Task> downloads = images
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(async image =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    string? path = await _images
                        .GetOrDownloadAsync(image, cancellationToken)
                        .ConfigureAwait(false);

                    if (path is not null)
                    {
                        Interlocked.Increment(ref downloaded);
                    }
                }
                finally
                {
                    gate.Release();

                    int done = Interlocked.Increment(ref processed);
                    if (done % 50 == 0)
                    {
                        progress?.Report(new CatalogImportProgress("Изображения", done, images.Count));
                    }
                }
            });

        await Task.WhenAll(downloads).ConfigureAwait(false);

        return downloaded;
    }

    private static DateTimeOffset? ParseFeedDate(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTimeOffset parsed)
            ? parsed
            : null;

    /// <summary>
    /// Обрезает значение по длине колонки: выгрузка формируется не нами,
    /// и слишком длинное описание не должно ронять сохранение.
    /// </summary>
    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    /// <summary>Предложение каталога в том виде, в каком оно прочитано из файла.</summary>
    private sealed class CatalogOffer
    {
        public string? Vendor { get; set; }

        public string? VendorCode { get; set; }

        public string? Description { get; set; }

        public string? Name { get; set; }

        public List<string> Pictures { get; } = [];

        public List<string> Barcodes { get; } = [];

        public Dictionary<string, string> Parameters { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Накопительные счётчики импорта.</summary>
    private sealed class ImportState
    {
        public DateTimeOffset? FeedDate { get; set; }

        public int Offers { get; set; }

        public int Cards { get; set; }

        public int WithImages { get; set; }

        public int WithBarcodes { get; set; }

        public int Skipped { get; set; }
    }
}
