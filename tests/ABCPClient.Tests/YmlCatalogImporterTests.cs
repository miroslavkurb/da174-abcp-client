using System.Net;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;
using ABCPClient.Infrastructure.Integration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет импорт каталога магазина: разбор выгрузки и заполнение кэша карточек
/// без единого обращения к API.
/// </summary>
public sealed class YmlCatalogImporterTests : IDisposable
{
    private readonly List<string> _temporaryFiles = [];

    private const string Feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <yml_catalog date="2026-07-24T20:03:32+03:00">
          <shop>
            <name>Дойч-Авто</name>
            <categories>
              <category id="1">Все товары</category>
            </categories>
            <offers>
              <offer id="a1" available="true">
                <url>https://da174.ru/parts/3Ton/40036</url>
                <price>225</price>
                <categoryId>7</categoryId>
                <picture>https://pubimg.nodacdn.net/images/09a73cde.jpeg</picture>
                <name>ХИМИЯ: Полироль 3Ton арт. 40036</name>
                <vendor>3Ton</vendor>
                <vendorCode>40036</vendorCode>
                <description>Полироль-восстановитель чёрного цвета 354 мл</description>
                <barcode>4607030880082</barcode>
                <param name="Тип">полироль</param>
                <param name="Применение">кузов (ЛКП)</param>
              </offer>
              <offer id="a2" available="true">
                <price>432</price>
                <name>СКОТЧ 3M 20 мм 3M арт. 20MM3M</name>
                <vendor>3M</vendor>
                <vendorCode>20MM3M</vendorCode>
                <description>Скотч 20 мм / 3 м</description>
              </offer>
              <offer id="a3" available="true">
                <price>100</price>
                <name>Без производителя</name>
                <vendorCode>NOBRAND</vendorCode>
              </offer>
            </offers>
          </shop>
        </yml_catalog>
        """;

    [Fact]
    public async Task Feed_fills_card_cache_without_touching_api()
    {
        InMemoryCards repository = new();
        CountingImages images = new();

        YmlCatalogImporter importer = CreateImporter(repository, images);

        CatalogImportResult result = await importer
            .ImportAsync(WriteFeed(Feed), cancellationToken: CancellationToken.None);

        Assert.Equal(3, result.Offers);
        Assert.Equal(2, result.Cards);
        Assert.Equal(1, result.WithImages);
        Assert.Equal(1, result.WithBarcodes);

        // Предложение без бренда пропускается: карточку не по чему опознать.
        Assert.Equal(1, result.Skipped);

        Assert.Equal(new DateTimeOffset(2026, 7, 24, 20, 3, 32, TimeSpan.FromHours(3)), result.FeedDate);

        // Изображения по умолчанию не скачиваются — только адрес сохраняется.
        Assert.Empty(images.Requested);
        Assert.Equal(0, result.ImagesDownloaded);

        ArticleCard card = repository.Stored["3ton|40036"];
        Assert.Equal("3Ton", card.Brand);
        Assert.Equal("40036", card.Number);
        Assert.Equal("Полироль-восстановитель чёрного цвета 354 мл", card.Description);
        Assert.Equal("https://pubimg.nodacdn.net/images/09a73cde.jpeg", card.ImageName);
        Assert.Equal("4607030880082", card.Barcodes);
        Assert.Equal(ArticleCardSource.Catalog, card.Source);
        Assert.False(card.NotFound);

        Dictionary<string, string> properties = Application.Services.ArticleCardProperties
            .Flatten(card.PropertiesJson)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        Assert.Equal("полироль", properties["Тип"]);
        Assert.Equal("кузов (ЛКП)", properties["Применение"]);

        // Описание берётся из description, а не из названия с брендом и артикулом в хвосте.
        Assert.Equal("Скотч 20 мм / 3 м", repository.Stored["3m|20mm3m"].Description);
    }

    [Fact]
    public async Task Imported_cards_replace_api_requests()
    {
        InMemoryCards repository = new();
        YmlCatalogImporter importer = CreateImporter(repository, new CountingImages());

        await importer.ImportAsync(WriteFeed(Feed), cancellationToken: CancellationToken.None);

        // После импорта служба карточек берёт данные из кэша: к API не обращается.
        IReadOnlyDictionary<string, ArticleCard> found = await repository.GetAsync(
            [new ArticleRef("3Ton", "40036"), new ArticleRef("3M", "20MM3M")]);

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task Images_are_prefetched_when_enabled()
    {
        InMemoryCards repository = new();
        CountingImages images = new();

        YmlCatalogImporter importer = CreateImporter(
            repository,
            images,
            new CatalogOptions { PrefetchImages = true });

        CatalogImportResult result = await importer
            .ImportAsync(WriteFeed(Feed), cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.ImagesDownloaded);
        Assert.Equal("https://pubimg.nodacdn.net/images/09a73cde.jpeg", Assert.Single(images.Requested));
    }

    [Fact]
    public async Task Missing_file_is_reported_clearly()
    {
        YmlCatalogImporter importer = CreateImporter(new InMemoryCards(), new CountingImages());

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            importer.ImportAsync(
                Path.Combine(Path.GetTempPath(), $"нет-такого-{Guid.NewGuid():N}.xml"),
                cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task Empty_path_is_rejected()
    {
        YmlCatalogImporter importer = CreateImporter(new InMemoryCards(), new CountingImages());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            importer.ImportAsync(cancellationToken: CancellationToken.None));
    }

    private YmlCatalogImporter CreateImporter(
        InMemoryCards repository,
        CountingImages images,
        CatalogOptions? options = null) =>
        new(
            repository,
            new CatalogSettings(options ?? new CatalogOptions()),
            new MemoryStore(),
            images,
            new NoNetworkHttpClientFactory(),
            NullLogger<YmlCatalogImporter>.Instance);

    private string WriteFeed(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"abcpclient-feed-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, content);
        _temporaryFiles.Add(path);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (string path in _temporaryFiles.Where(File.Exists))
        {
            File.Delete(path);
        }
    }

    private sealed class InMemoryCards : IArticleCardRepository
    {
        public Dictionary<string, ArticleCard> Stored { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyDictionary<string, ArticleCard>> GetAsync(
            IReadOnlyCollection<ArticleRef> articles,
            CancellationToken cancellationToken = default)
        {
            Dictionary<string, ArticleCard> found = new(StringComparer.Ordinal);

            foreach (ArticleRef article in articles)
            {
                if (Stored.TryGetValue(article.Key, out ArticleCard? card))
                {
                    found[article.Key] = card;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, ArticleCard>>(found);
        }

        public Task UpsertAsync(IReadOnlyCollection<ArticleCard> cards, CancellationToken cancellationToken = default)
        {
            foreach (ArticleCard card in cards)
            {
                Stored[new ArticleRef(card.Brand, card.Number).Key] = card;
            }

            return Task.CompletedTask;
        }

        public Task<ArticleCard?> FindByBarcodeAsync(
            string barcode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.Values.FirstOrDefault(card =>
                card.Barcodes is { Length: > 0 } codes
                && codes.Split(';').Contains(barcode, StringComparer.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<ArticleCard>> SearchAsync(
            string query,
            int limit = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArticleCard>>(Stored.Values
                .Where(card => card.Number.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToArray());
    }

    private sealed class CountingImages : IProductImageCache
    {
        public List<string> Requested { get; } = [];

        public Task<string?> GetOrDownloadAsync(string imageName, CancellationToken cancellationToken = default)
        {
            lock (Requested)
            {
                Requested.Add(imageName);
            }

            return Task.FromResult<string?>(@"C:\cache\" + Requested.Count + ".jpeg");
        }
    }

    private sealed class CatalogSettings : IAbcpSettingsProvider
    {
        private readonly CatalogOptions _catalog;

        public CatalogSettings(CatalogOptions catalog) => _catalog = catalog;

        public Task<AbcpApiOptions> GetApiOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AbcpApiOptions());

        public Task<SyncOptions> GetSyncOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncOptions());

        public Task<CatalogOptions> GetCatalogOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_catalog);

        public Task<UpdateOptions> GetUpdateOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateOptions());

        public Task<PickingOptions> GetPickingOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PickingOptions());
    }

    private sealed class MemoryStore : IAppSettingsStore
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out string? value) ? value : null);

        public Task SetAsync(
            string key,
            string? value,
            bool protect = false,
            CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string?>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string?>>(_values);

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.Remove(key));
    }

    /// <summary>
    /// Фабрика, которая падает при любой попытке выйти в сеть: импорт из файла
    /// не должен обращаться никуда.
    /// </summary>
    private sealed class NoNetworkHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FailingHandler());

        private sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                throw new HttpRequestException(
                    "Импорт из файла не должен обращаться в сеть",
                    null,
                    HttpStatusCode.BadGateway);
        }
    }
}
