using System.Net;
using System.Security.Cryptography;
using System.Text;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Models;
using ABCPClient.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет проверку и загрузку обновлений из релизов GitHub.
/// </summary>
public sealed class GitHubUpdateServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"abcpclient-updates-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("miroslavkurb/da174-abcp-client", "miroslavkurb", "da174-abcp-client")]
    [InlineData("  owner/name  ", "owner", "name")]
    [InlineData("https://github.com/owner/name", "owner", "name")]
    [InlineData("https://github.com/owner/name.git", "owner", "name")]
    [InlineData("/owner/name/", "owner", "name")]
    public void Repository_is_parsed(string value, string expectedOwner, string expectedName)
    {
        Assert.True(GitHubUpdateService.TryParseRepository(value, out string? owner, out string? name));

        Assert.Equal(expectedOwner, owner);
        Assert.Equal(expectedName, name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("owner")]
    [InlineData("owner/name/extra")]
    public void Bad_repository_is_rejected(string? value) =>
        Assert.False(GitHubUpdateService.TryParseRepository(value, out _, out _));

    [Theory]
    [InlineData("ABCPClient-1.0.0-win-x64.exe", "*win-x64.exe", true)]
    [InlineData("ABCPClient-1.0.0-win-x64-runtime-required.zip", "*win-x64.exe", false)]
    [InlineData("SHA256SUMS.txt", "*win-x64.exe", false)]
    [InlineData("abcpclient-1.0.0-WIN-X64.EXE", "*win-x64.exe", true)]
    [InlineData("ABCPClient.exe", "ABCPClient*", true)]
    [InlineData("anything", "*", true)]
    public void Asset_mask_is_applied(string name, string pattern, bool expected) =>
        Assert.Equal(expected, GitHubUpdateService.Matches(name, pattern));

    [Fact]
    public void Checksums_are_parsed_in_sha256sum_format()
    {
        const string content = """
            6a5a8f4cea73203f3e776cd96d34c392305b5a81329dfd3a71f1ecb1ff8f8d4c  ABCPClient-1.0.0-win-x64-runtime-required.zip
            b71acb3bc4e5e1df7975e431180dd2b1602612161cbfc57b82b2b77e6e2fff1a  ABCPClient-1.0.0-win-x64.exe
            """;

        Assert.Equal(
            "b71acb3bc4e5e1df7975e431180dd2b1602612161cbfc57b82b2b77e6e2fff1a",
            GitHubUpdateService.ParseChecksums(content, "ABCPClient-1.0.0-win-x64.exe"));

        Assert.Null(GitHubUpdateService.ParseChecksums(content, "нет-такого.exe"));
    }

    [Fact]
    public void Binary_marker_before_name_is_tolerated()
    {
        // Утилита sha256sum помечает двоичные файлы звёздочкой перед именем.
        Assert.Equal("aabb", GitHubUpdateService.ParseChecksums("aabb *file.exe", "file.exe"));
    }

    [Fact]
    public void Newest_release_wins_regardless_of_order()
    {
        GitHubReleaseDto[] releases =
        [
            Release("v1.0.0"),
            Release("v1.2.0"),
            Release("v1.1.0"),
        ];

        AvailableUpdate? update = GitHubUpdateService.SelectNewest(releases, new UpdateOptions());

        Assert.Equal("1.2.0", update!.Version.Display);
    }

    [Fact]
    public void Drafts_and_releases_without_asset_are_skipped()
    {
        GitHubReleaseDto draft = Release("v3.0.0");
        draft.Draft = true;

        GitHubReleaseDto noAsset = Release("v2.0.0");
        noAsset.Assets.Clear();

        AvailableUpdate? update = GitHubUpdateService.SelectNewest(
            [draft, noAsset, Release("v1.0.0")],
            new UpdateOptions());

        Assert.Equal("1.0.0", update!.Version.Display);
    }

    [Fact]
    public void Prereleases_are_skipped_unless_allowed()
    {
        GitHubReleaseDto beta = Release("v2.0.0-beta.1");
        beta.Prerelease = true;

        GitHubReleaseDto[] releases = [beta, Release("v1.0.0")];

        Assert.Equal("1.0.0", GitHubUpdateService.SelectNewest(releases, new UpdateOptions())!.Version.Display);

        AvailableUpdate? withPrerelease = GitHubUpdateService.SelectNewest(
            releases,
            new UpdateOptions { IncludePrerelease = true });

        Assert.Equal("2.0.0-beta.1", withPrerelease!.Version.Display);
        Assert.True(withPrerelease.IsPrerelease);
    }

    [Fact]
    public void Tag_that_is_not_a_version_is_skipped()
    {
        AvailableUpdate? update = GitHubUpdateService.SelectNewest(
            [Release("latest"), Release("v1.0.0")],
            new UpdateOptions());

        Assert.Equal("1.0.0", update!.Version.Display);
    }

    [Fact]
    public async Task Check_reports_disabled_when_repository_is_not_set()
    {
        // Репозиторий по умолчанию задан в коде, поэтому «не задан» — это явно пустое значение.
        GitHubUpdateService service = CreateService(
            new StubHandler(),
            new UpdateOptions { Repository = string.Empty });

        UpdateCheckResult result = await service.CheckAsync(force: true);

        Assert.Equal(UpdateCheckOutcome.Disabled, result.Outcome);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Automatic_check_respects_the_interval()
    {
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.OK, ReleasesJson("v9.9.9"));

        MemoryStore store = new();
        FixedTime time = new(DateTimeOffset.Parse("2026-07-26T12:00:00Z", null));

        GitHubUpdateService service = CreateService(handler, Options(), store, time);

        await service.CheckAsync();
        Assert.Single(handler.Requests);

        // Второй запуск в тот же час к GitHub не обращается.
        UpdateCheckResult skipped = await service.CheckAsync();
        Assert.Equal(UpdateCheckOutcome.Skipped, skipped.Outcome);
        Assert.Single(handler.Requests);

        // Проверка по кнопке ограничение не соблюдает.
        handler.Enqueue(HttpStatusCode.OK, ReleasesJson("v9.9.9"));
        await service.CheckAsync(force: true);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Failed_check_does_not_postpone_the_next_one()
    {
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.NotFound, """{ "message": "Not Found" }""");

        MemoryStore store = new();
        GitHubUpdateService service = CreateService(handler, Options(), store);

        UpdateCheckResult result = await service.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
        Assert.Contains("токен", result.Message, StringComparison.OrdinalIgnoreCase);

        // Момент проверки не записан, поэтому следующая попытка пойдёт сразу.
        Assert.False(store.Values.ContainsKey(AppSettingKeys.UpdatesLastCheckAt));
    }

    [Fact]
    public async Task Token_is_sent_only_when_configured()
    {
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.OK, ReleasesJson("v0.0.1"));

        await CreateService(handler, Options()).CheckAsync(force: true);
        Assert.Null(handler.Requests[0].Authorization);

        handler.Enqueue(HttpStatusCode.OK, ReleasesJson("v0.0.1"));
        UpdateOptions withToken = Options();
        withToken.Token = "ghp_secret";

        await CreateService(handler, withToken).CheckAsync(force: true);
        Assert.Equal("Bearer ghp_secret", handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task Download_verifies_checksum()
    {
        byte[] payload = Encoding.UTF8.GetBytes("это как бы новая версия программы");
        string hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.OK, payload);
        handler.Enqueue(HttpStatusCode.OK, $"{hash}  ABCPClient-2.0.0-win-x64.exe");

        GitHubUpdateService service = CreateService(handler, Options());

        List<UpdateDownloadProgress> reports = [];
        DownloadedUpdate downloaded = await service.DownloadAsync(
            Update("2.0.0"),
            new Progress<UpdateDownloadProgress>(reports.Add));

        Assert.True(downloaded.ChecksumVerified);
        Assert.Equal(payload, await File.ReadAllBytesAsync(downloaded.FilePath));
        Assert.NotEmpty(reports);

        // Недокачанный файл не должен остаться в каталоге.
        Assert.Empty(Directory.GetFiles(_directory, "*.part"));
    }

    [Fact]
    public async Task Download_with_wrong_checksum_is_rejected_and_file_removed()
    {
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.OK, Encoding.UTF8.GetBytes("подменённый файл"));
        handler.Enqueue(HttpStatusCode.OK, "0000000000000000000000000000000000000000000000000000000000000000  ABCPClient-2.0.0-win-x64.exe");

        GitHubUpdateService service = CreateService(handler, Options());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadAsync(Update("2.0.0")));

        Assert.Contains("не совпала", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Download_without_checksum_asset_is_marked_unverified()
    {
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.OK, Encoding.UTF8.GetBytes("файл без сумм"));

        GitHubUpdateService service = CreateService(handler, Options());

        DownloadedUpdate downloaded = await service.DownloadAsync(Update("2.0.0", withChecksums: false));

        // Файл сохранён, но установщик такой запускать откажется.
        Assert.False(downloaded.ChecksumVerified);
        Assert.True(File.Exists(downloaded.FilePath));
    }

    [Fact]
    public async Task Missing_line_in_checksums_cancels_the_update()
    {
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.OK, Encoding.UTF8.GetBytes("файл"));
        handler.Enqueue(HttpStatusCode.OK, "aaaa  какой-то-другой-файл.exe");

        GitHubUpdateService service = CreateService(handler, Options());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync(Update("2.0.0")));
    }

    private static UpdateOptions Options() => new()
    {
        Repository = "miroslavkurb/da174-abcp-client",
        CheckIntervalHours = 6,
    };

    private static AvailableUpdate Update(string version, bool withChecksums = true) => new(
        AppVersion.Parse(version),
        $"v{version}",
        $"ABCP Client v{version}",
        "Заметки",
        DateTimeOffset.Parse("2026-07-26T12:00:00Z", null),
        false,
        $"ABCPClient-{version}-win-x64.exe",
        42,
        "https://api.github.com/repos/o/n/releases/assets/1",
        withChecksums ? "https://api.github.com/repos/o/n/releases/assets/2" : null,
        "https://github.com/o/n/releases/tag/v" + version);

    private static GitHubReleaseDto Release(string tag) => new()
    {
        TagName = tag,
        Name = "ABCP Client " + tag,
        HtmlUrl = "https://github.com/o/n/releases/tag/" + tag,
        Assets =
        [
            new GitHubAssetDto
            {
                Name = $"ABCPClient-{tag.TrimStart('v')}-win-x64.exe",
                Size = 1024,
                Url = "https://api.github.com/repos/o/n/releases/assets/1",
            },
            new GitHubAssetDto
            {
                Name = "SHA256SUMS.txt",
                Size = 128,
                Url = "https://api.github.com/repos/o/n/releases/assets/2",
            },
        ],
    };

    private static string ReleasesJson(string tag) => $$"""
        [
          {
            "tag_name": "{{tag}}",
            "name": "ABCP Client {{tag}}",
            "body": "Заметки к выпуску",
            "draft": false,
            "prerelease": false,
            "published_at": "2026-07-26T09:25:14Z",
            "html_url": "https://github.com/o/n/releases/tag/{{tag}}",
            "assets": [
              {
                "name": "ABCPClient-{{tag.TrimStart('v')}}-win-x64.exe",
                "size": 89512345,
                "url": "https://api.github.com/repos/o/n/releases/assets/1"
              },
              {
                "name": "SHA256SUMS.txt",
                "size": 178,
                "url": "https://api.github.com/repos/o/n/releases/assets/2"
              }
            ]
          }
        ]
        """;

    private GitHubUpdateService CreateService(
        StubHandler handler,
        UpdateOptions options,
        IAppSettingsStore? store = null,
        TimeProvider? time = null)
    {
        Directory.CreateDirectory(_directory);

        return new GitHubUpdateService(
            new SingleClientFactory(handler),
            new UpdateSettings(options),
            store ?? new MemoryStore(),
            NullLogger<GitHubUpdateService>.Instance)
        {
            DownloadDirectory = _directory,
            Time = time ?? TimeProvider.System,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>Обработчик, отдающий заготовленные ответы по очереди.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _replies = new();

        public List<(Uri Uri, string? Authorization, string? Accept)> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status, string body) =>
            _replies.Enqueue(new HttpResponseMessage(status) { Content = new StringContent(body) });

        public void Enqueue(HttpStatusCode status, byte[] body) =>
            _replies.Enqueue(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.Accept.ToString()));

            if (_replies.Count == 0)
            {
                throw new HttpRequestException("Лишний запрос: заготовленные ответы закончились");
            }

            return Task.FromResult(_replies.Dequeue());
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class UpdateSettings : IAbcpSettingsProvider
    {
        private readonly UpdateOptions _updates;

        public UpdateSettings(UpdateOptions updates) => _updates = updates;

        public Task<AbcpApiOptions> GetApiOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AbcpApiOptions());

        public Task<SyncOptions> GetSyncOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncOptions());

        public Task<CatalogOptions> GetCatalogOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogOptions());

        public Task<UpdateOptions> GetUpdateOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_updates);
    }

    private sealed class MemoryStore : IAppSettingsStore
    {
        public Dictionary<string, string?> Values { get; } = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.TryGetValue(key, out string? value) ? value : null);

        public Task SetAsync(
            string key,
            string? value,
            bool protect = false,
            CancellationToken cancellationToken = default)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string?>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string?>>(Values);

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.Remove(key));
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTime(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
