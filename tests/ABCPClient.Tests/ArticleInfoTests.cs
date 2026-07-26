using System.Net;
using System.Text;
using System.Text.Json;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Application.Serialization;
using ABCPClient.Infrastructure.Api;
using Microsoft.Extensions.Logging.Abstractions;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет получение карточек товаров и кэш изображений.
/// </summary>
public sealed class ArticleInfoTests : IDisposable
{
    private const string PasswordHash = "0123456789abcdef0123456789abcdef";

    private readonly List<string> _createdFiles = [];

    private static AbcpApiClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            new StubSettings(new AbcpApiOptions
            {
                BaseUrl = "https://demo.public.api.abcp.ru",
                Login = "api-admin",
                PasswordMd5 = PasswordHash,
                TimeoutSeconds = 5,
                RetryCount = 0,
            }),
            NullLogger<AbcpApiClient>.Instance)
        {
            RetryBaseDelay = TimeSpan.Zero,
        };

    [Fact]
    public void Article_card_is_parsed_with_images_and_properties()
    {
        const string json = """
        [
            {
                "brand": "Shell",
                "number": "550051529",
                "descr": "Масло моторное синтетика 5W-40 4 л.",
                "images": [ { "name": "099abad5db73fb63.jpeg", "order": 0 } ],
                "images_count": 1,
                "properties": {
                    "Вязкость": "5W-40",
                    "Объём, л.": "4",
                    "Допуски производителей": [ "MB 229.3", "VW 502 00" ]
                }
            }
        ]
        """;

        List<ArticleInfoDto>? cards = JsonSerializer.Deserialize<List<ArticleInfoDto>>(json, AbcpJson.Options);

        ArticleInfoDto card = Assert.Single(cards!);
        Assert.Equal("Shell", card.Brand);
        Assert.Equal(1, card.ImagesCount);
        Assert.Equal("099abad5db73fb63.jpeg", Assert.Single(card.Images).Name);

        Dictionary<string, string> properties = card.GetFlatProperties()
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        Assert.Equal("5W-40", properties["Вязкость"]);

        // Значение-массив склеивается в строку: в списке позиций оно выводится одной строкой.
        Assert.Equal("MB 229.3, VW 502 00", properties["Допуски производителей"]);
    }

    [Fact]
    public async Task Articles_info_is_sent_as_post_form_without_secrets_in_url()
    {
        RecordingHandler handler = new("[]");
        AbcpApiClient client = CreateClient(handler);

        await client.GetArticlesInfoAsync(
        [
            new ArticleRef("Shell", "550051529"),
            new ArticleRef("LUKOIL", "3148675"),

            // Дубликат не должен попасть в запрос.
            new ArticleRef("shell", "550051529"),
        ]);

        Assert.Single(handler.Requests);
        (HttpMethod method, Uri uri, string body) = handler.Requests[0];

        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/cp/articles/info/batch", uri.AbsolutePath);

        // Реквизиты уходят телом запроса, в строке запроса их нет вообще.
        Assert.Equal(string.Empty, uri.Query);

        string decoded = Uri.UnescapeDataString(body);
        Assert.Contains($"userpsw={PasswordHash}", decoded, StringComparison.Ordinal);
        Assert.Contains("articles[0][brand]=Shell", decoded, StringComparison.Ordinal);
        Assert.Contains("articles[0][number]=550051529", decoded, StringComparison.Ordinal);
        Assert.Contains("articles[1][brand]=LUKOIL", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("articles[2]", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Articles_info_is_split_into_batches_of_hundred()
    {
        RecordingHandler handler = new("[]");
        AbcpApiClient client = CreateClient(handler);

        ArticleRef[] articles = Enumerable.Range(1, 150)
            .Select(index => new ArticleRef("Febi", index.ToString()))
            .ToArray();

        await client.GetArticlesInfoAsync(articles);

        // Ограничение API — 100 деталей на запрос.
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("articles[99]", Uri.UnescapeDataString(handler.Requests[0].Body), StringComparison.Ordinal);
        Assert.DoesNotContain("articles[100]", Uri.UnescapeDataString(handler.Requests[0].Body), StringComparison.Ordinal);
        Assert.Contains("articles[49]", Uri.UnescapeDataString(handler.Requests[1].Body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_batch_operation_falls_back_to_single_requests()
    {
        // Часть сайтов не поддерживает cp/articles/info/batch и отвечает ошибкой 3.
        SequenceHandler handler = new(
            new StubReply(HttpStatusCode.BadRequest, """{ "errorCode": 3, "errorMessage": "Unknown operation" }"""),
            new StubReply(HttpStatusCode.OK, """
                {
                    "brand": "ACQ",
                    "descr": "Клапан кондиционера",
                    "images": [ { "name": "11d346aed8bd.jpeg", "order": 0 } ],
                    "images_count": 1,
                    "number": "ADW0855",
                    "outer_number": "ADW-0855"
                }
                """),
            new StubReply(HttpStatusCode.OK, """{ "brand": "Febi", "number": "01089", "images": [] }"""));

        AbcpApiClient client = CreateClient(handler);

        IReadOnlyList<ArticleInfoDto> cards = await client.GetArticlesInfoAsync(
            [new ArticleRef("ACQ", "ADW-0855"), new ArticleRef("Febi", "01089")]);

        Assert.Equal(2, cards.Count);
        Assert.Equal("11d346aed8bd.jpeg", Assert.Single(cards[0].Images).Name);

        // Первый запрос — пакетный POST, затем по одному GET на деталь.
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("/articles/info", handler.Requests[1].Uri.AbsolutePath);

        string query = Uri.UnescapeDataString(handler.Requests[1].Uri.Query);
        Assert.Contains("brand=ACQ", query, StringComparison.Ordinal);
        Assert.Contains("number=ADW-0855", query, StringComparison.Ordinal);
        Assert.Contains("format=bnpi", query, StringComparison.Ordinal);

        // Повторный вызов уже не пробует пакетную операцию.
        await client.GetArticlesInfoAsync([new ArticleRef("ACQ", "ADW-0855")]);
        Assert.Equal(HttpMethod.Get, handler.Requests[3].Method);
    }

    [Fact]
    public async Task Card_is_matched_by_original_and_normalized_number()
    {
        SequenceHandler handler = new(
            new StubReply(HttpStatusCode.BadRequest, """{ "errorCode": 3, "errorMessage": "Unknown operation" }"""),
            new StubReply(HttpStatusCode.OK, """
                { "brand": "ACQ", "number": "ADW0855", "outer_number": "ADW-0855", "images": [] }
                """));

        AbcpApiClient client = CreateClient(handler);

        ArticleInfoDto card = Assert.Single(
            await client.GetArticlesInfoAsync([new ArticleRef("ACQ", "ADW-0855")]));

        // Ключи сопоставления содержат оба написания номера.
        Assert.Contains(new ArticleRef("ACQ", "ADW0855").Key, card.GetMatchKeys());
        Assert.Contains(new ArticleRef("ACQ", "ADW-0855").Key, card.GetMatchKeys());
    }

    [Fact]
    public async Task Missing_card_does_not_break_other_positions()
    {
        SequenceHandler handler = new(
            new StubReply(HttpStatusCode.BadRequest, """{ "errorCode": 3, "errorMessage": "Unknown operation" }"""),
            new StubReply(HttpStatusCode.NotFound, """{ "errorCode": 301, "errorMessage": "Object not found" }"""),
            new StubReply(HttpStatusCode.OK, """{ "brand": "Febi", "number": "01089", "images": [] }"""));

        AbcpApiClient client = CreateClient(handler);

        IReadOnlyList<ArticleInfoDto> cards = await client.GetArticlesInfoAsync(
            [new ArticleRef("НетТакого", "000"), new ArticleRef("Febi", "01089")]);

        Assert.Equal("Febi", Assert.Single(cards).Brand);
    }

    [Fact]
    public async Task Empty_article_list_does_not_call_api()
    {
        RecordingHandler handler = new("[]");
        AbcpApiClient client = CreateClient(handler);

        Assert.Empty(await client.GetArticlesInfoAsync([]));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Image_is_downloaded_once_and_reused_from_disk()
    {
        RecordingHandler handler = new(Encoding.UTF8.GetBytes("картинка"));
        ProductImageCache cache = new(
            new SingleClientFactory(handler),
            NullLogger<ProductImageCache>.Instance);

        string name = $"test-{Guid.NewGuid():N}.jpeg";

        string? first = await cache.GetOrDownloadAsync(name);
        Assert.NotNull(first);
        _createdFiles.Add(first);
        Assert.True(File.Exists(first));

        string? second = await cache.GetOrDownloadAsync(name);

        Assert.Equal(first, second);

        // Второй раз файл берётся с диска, запрос не повторяется.
        Assert.Single(handler.Requests);
        Assert.Equal(
            ProductImageCache.ImageBaseUrl + name,
            handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task Missing_image_returns_null_without_creating_file()
    {
        RecordingHandler handler = new(HttpStatusCode.NotFound);
        ProductImageCache cache = new(
            new SingleClientFactory(handler),
            NullLogger<ProductImageCache>.Instance);

        Assert.Null(await cache.GetOrDownloadAsync("нет-такой.jpeg"));
        Assert.Null(await cache.GetOrDownloadAsync(null!));
        Assert.Null(await cache.GetOrDownloadAsync("   "));
    }

    [Fact]
    public async Task Image_name_with_path_traversal_is_reduced_to_file_name()
    {
        RecordingHandler handler = new(Encoding.UTF8.GetBytes("картинка"));
        ProductImageCache cache = new(
            new SingleClientFactory(handler),
            NullLogger<ProductImageCache>.Instance);

        string name = $"traversal-{Guid.NewGuid():N}.jpeg";

        // Имя приходит из внешнего ответа: запись не должна уйти за пределы кэша.
        string? path = await cache.GetOrDownloadAsync($"../../{name}");

        Assert.NotNull(path);
        _createdFiles.Add(path);

        string expectedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABCPClient",
            "images");

        Assert.Equal(expectedDirectory, Path.GetDirectoryName(path));
        Assert.Equal(name, Path.GetFileName(path));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (string path in _createdFiles.Where(File.Exists))
        {
            File.Delete(path);
        }
    }

    /// <summary>Обработчик, записывающий запросы и отдающий заготовленный ответ.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly byte[] _body;
        private readonly string _mediaType;

        public RecordingHandler(string json)
        {
            _statusCode = HttpStatusCode.OK;
            _body = Encoding.UTF8.GetBytes(json);
            _mediaType = "application/json";
        }

        public RecordingHandler(byte[] content)
        {
            _statusCode = HttpStatusCode.OK;
            _body = content;
            _mediaType = "image/jpeg";
        }

        public RecordingHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
            _body = [];
            _mediaType = "text/plain";
        }

        public List<(HttpMethod Method, Uri Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            Requests.Add((request.Method, request.RequestUri!, body));

            HttpResponseMessage response = new(_statusCode)
            {
                Content = new ByteArrayContent(_body),
            };

            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_mediaType);
            return response;
        }
    }

    /// <summary>Заготовленный ответ для последовательности запросов.</summary>
    private sealed record StubReply(HttpStatusCode StatusCode, string Body);

    /// <summary>Обработчик, отдающий ответы по порядку и запоминающий запросы.</summary>
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<StubReply> _replies;
        private readonly StubReply _last;

        public SequenceHandler(params StubReply[] replies)
        {
            _replies = new Queue<StubReply>(replies);
            _last = replies[^1];
        }

        public List<(HttpMethod Method, Uri Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            Requests.Add((request.Method, request.RequestUri!, body));

            StubReply reply = _replies.Count > 0 ? _replies.Dequeue() : _last;

            return new HttpResponseMessage(reply.StatusCode)
            {
                Content = new StringContent(reply.Body, Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>Фабрика, всегда отдающая клиент с подставным обработчиком.</summary>
    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    /// <summary>Поставщик настроек с фиксированными значениями.</summary>
    private sealed class StubSettings : IAbcpSettingsProvider
    {
        private readonly AbcpApiOptions _options;

        public StubSettings(AbcpApiOptions options) => _options = options;

        public Task<AbcpApiOptions> GetApiOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_options);

        public Task<SyncOptions> GetSyncOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncOptions());

        public Task<CatalogOptions> GetCatalogOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogOptions());

        public Task<UpdateOptions> GetUpdateOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateOptions());
    }
}
