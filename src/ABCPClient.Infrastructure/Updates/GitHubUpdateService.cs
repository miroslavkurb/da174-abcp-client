using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Infrastructure.Updates;

/// <summary>
/// Проверка и загрузка обновлений из релизов GitHub.
/// </summary>
/// <remarks>
/// Файлы выпуска собирает рабочий процесс <c>release.yml</c>: самодостаточный
/// <c>ABCPClient-{версия}-win-x64.exe</c> и <c>SHA256SUMS.txt</c>. Загруженный файл
/// сверяется с суммой из выпуска: без этого приложение запускало бы у себя
/// произвольный файл, полученный по сети.
/// Токен нужен только приватному репозиторию и берётся из настроек, а не из кода:
/// в раздаваемом исполняемом файле его быть не должно.
/// </remarks>
public sealed class GitHubUpdateService : IUpdateService
{
    /// <summary>Имя клиента <c>IHttpClientFactory</c> для GitHub.</summary>
    public const string HttpClientName = "github-updates";

    private const string ApiBaseUrl = "https://api.github.com";

    /// <summary>Сколько последних выпусков просматривать.</summary>
    private const int ReleasesToInspect = 20;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAbcpSettingsProvider _settings;
    private readonly IAppSettingsStore _store;
    private readonly ILogger<GitHubUpdateService> _logger;

    /// <summary>Создаёт службу обновлений.</summary>
    public GitHubUpdateService(
        IHttpClientFactory httpClientFactory,
        IAbcpSettingsProvider settings,
        IAppSettingsStore store,
        ILogger<GitHubUpdateService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _store = store;
        _logger = logger;

        CurrentVersion = ReadCurrentVersion();
    }

    /// <inheritdoc />
    public AppVersion CurrentVersion { get; }

    /// <summary>Источник времени. Отдельным свойством — ради предсказуемости тестов.</summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Куда складывать загруженные файлы.</summary>
    internal string DownloadDirectory { get; set; } = AppPaths.UpdatesDirectory;

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        UpdateOptions options = await _settings.GetUpdateOptionsAsync(cancellationToken).ConfigureAwait(false);

        if (!TryParseRepository(options.Repository, out string? owner, out string? name)
            || owner is null
            || name is null)
        {
            return new UpdateCheckResult(
                UpdateCheckOutcome.Disabled,
                CurrentVersion,
                null,
                "Репозиторий обновлений не задан");
        }

        if (!force && await IsTooSoonAsync(options, cancellationToken).ConfigureAwait(false))
        {
            return new UpdateCheckResult(
                UpdateCheckOutcome.Skipped,
                CurrentVersion,
                null,
                "Проверка выполнялась недавно");
        }

        try
        {
            GitHubReleaseDto[] releases = await GetReleasesAsync(owner, name, options, cancellationToken)
                .ConfigureAwait(false);

            // Автоматическая проверка отмечается только после успешного обращения:
            // иначе неудача откладывала бы следующую попытку на часы.
            await _store.SetAsync(
                    AppSettingKeys.UpdatesLastCheckAt,
                    Time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            AvailableUpdate? newest = SelectNewest(releases, options);

            if (newest is null)
            {
                return new UpdateCheckResult(
                    UpdateCheckOutcome.UpToDate,
                    CurrentVersion,
                    null,
                    "Подходящих выпусков не найдено");
            }

            if (newest.Version <= CurrentVersion)
            {
                return new UpdateCheckResult(UpdateCheckOutcome.UpToDate, CurrentVersion, null);
            }

            _logger.LogInformation(
                "Доступно обновление {Version} (установлена {Current})",
                newest.Version,
                CurrentVersion);

            return new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, CurrentVersion, newest);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Не удалось проверить обновления");

            return new UpdateCheckResult(
                UpdateCheckOutcome.Failed,
                CurrentVersion,
                null,
                Explain(exception));
        }
    }

    /// <inheritdoc />
    public async Task<DownloadedUpdate> DownloadAsync(
        AvailableUpdate update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        UpdateOptions options = await _settings.GetUpdateOptionsAsync(cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(DownloadDirectory);

        // Имя берётся из выпуска, но приводится к имени файла: значение пришло
        // извне и не должно уводить запись за пределы каталога.
        string fileName = Path.GetFileName(update.AssetName);
        if (string.IsNullOrEmpty(fileName))
        {
            throw new InvalidOperationException("Пустое имя файла обновления");
        }

        string target = Path.Combine(DownloadDirectory, fileName);
        string temporary = target + ".part";

        HttpClient client = CreateClient(options);

        progress?.Report(new UpdateDownloadProgress("Загрузка", 0, update.AssetSize));

        using (HttpRequestMessage request = new(HttpMethod.Get, update.AssetUrl))
        {
            // Без этого заголовка API вернёт описание вложения в JSON, а не сам файл.
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

            long? total = response.Content.Headers.ContentLength ?? update.AssetSize;

            await using FileStream file = new(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous);

            await using Stream source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            byte[] buffer = new byte[128 * 1024];
            long received = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

                received += read;
                progress?.Report(new UpdateDownloadProgress("Загрузка", received, total));
            }
        }

        progress?.Report(new UpdateDownloadProgress("Проверка контрольной суммы", update.AssetSize, update.AssetSize));

        bool verified = await VerifyChecksumAsync(temporary, fileName, update, options, cancellationToken)
            .ConfigureAwait(false);

        // Готовый файл появляется под своим именем только после проверки:
        // иначе установка могла бы подхватить недокачанный или подменённый файл.
        File.Move(temporary, target, overwrite: true);

        _logger.LogInformation(
            "Обновление {Version} загружено в {Path}, контрольная сумма {Verified}",
            update.Version,
            target,
            verified ? "сверена" : "не проверена");

        return new DownloadedUpdate(target, update.Version, verified);
    }

    /// <summary>
    /// Сверяет контрольную сумму загруженного файла с суммой из выпуска.
    /// </summary>
    /// <remarks>
    /// Приложение собирается запустить этот файл, поэтому расхождение — отказ,
    /// а не предупреждение. Отсутствие файла сумм в выпуске отказом не считается:
    /// старые выпуски его не содержали.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Сумма не совпала.</exception>
    private async Task<bool> VerifyChecksumAsync(
        string path,
        string fileName,
        AvailableUpdate update,
        UpdateOptions options,
        CancellationToken cancellationToken)
    {
        if (update.ChecksumUrl is null)
        {
            _logger.LogWarning(
                "В выпуске {Tag} нет файла {Asset} — проверить загруженный файл нечем",
                update.TagName,
                options.ChecksumAssetName);

            return false;
        }

        string expected;
        try
        {
            expected = await ReadExpectedHashAsync(update.ChecksumUrl, fileName, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            File.Delete(path);

            throw new InvalidOperationException(
                "Не удалось получить файл контрольных сумм выпуска, установка отменена",
                exception);
        }

        string actual;
        await using (FileStream file = File.OpenRead(path))
        {
            byte[] hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
            actual = Convert.ToHexString(hash).ToLowerInvariant();
        }

        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);

            throw new InvalidOperationException(
                $"Контрольная сумма файла обновления не совпала: ожидалась {expected}, получена {actual}. "
                    + "Файл удалён, установка отменена");
        }

        return true;
    }

    private async Task<string> ReadExpectedHashAsync(
        string checksumUrl,
        string fileName,
        UpdateOptions options,
        CancellationToken cancellationToken)
    {
        HttpClient client = CreateClient(options);

        using HttpRequestMessage request = new(HttpMethod.Get, checksumUrl);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        using HttpResponseMessage response = await client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return ParseChecksums(content, fileName)
            ?? throw new InvalidOperationException(
                $"В файле контрольных сумм нет строки для {fileName}, установка отменена");
    }

    /// <summary>
    /// Ищет сумму нужного файла в формате <c>sha256sum</c>: «хэш  имя файла».
    /// </summary>
    internal static string? ParseChecksums(string content, string fileName)
    {
        foreach (string line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Trim().Split(
                (char[])[' ', '\t'],
                2,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length != 2)
            {
                continue;
            }

            // В формате sha256sum перед именем может стоять «*» — признак двоичного файла.
            string name = parts[1].TrimStart('*');

            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0].ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// Выбирает самый новый подходящий выпуск.
    /// </summary>
    /// <remarks>
    /// Порядок выпусков в ответе — по дате публикации, а не по версии, поэтому
    /// сравнение идёт по разобранной версии. Черновики пропускаются: их файлов
    /// ещё нет. Выпуск без нужного вложения тоже пропускается — обновляться нечем.
    /// </remarks>
    internal static AvailableUpdate? SelectNewest(IEnumerable<GitHubReleaseDto> releases, UpdateOptions options)
    {
        AvailableUpdate? best = null;

        foreach (GitHubReleaseDto release in releases)
        {
            if (release.Draft)
            {
                continue;
            }

            if (release.Prerelease && !options.IncludePrerelease)
            {
                continue;
            }

            if (!AppVersion.TryParse(release.TagName, out AppVersion? version))
            {
                continue;
            }

            GitHubAssetDto? asset = release.Assets
                .FirstOrDefault(candidate => Matches(candidate.Name, options.AssetPattern));

            if (asset is null)
            {
                continue;
            }

            if (best is not null && version <= best.Version)
            {
                continue;
            }

            GitHubAssetDto? checksums = release.Assets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, options.ChecksumAssetName, StringComparison.OrdinalIgnoreCase));

            best = new AvailableUpdate(
                version,
                release.TagName,
                release.Name,
                release.Body,
                release.PublishedAt,
                release.Prerelease,
                asset.Name,
                asset.Size,
                asset.Url,
                checksums?.Url,
                release.HtmlUrl ?? string.Empty);
        }

        return best;
    }

    /// <summary>
    /// Сопоставляет имя файла с маской вида <c>*win-x64.exe</c>.
    /// </summary>
    internal static bool Matches(string name, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        string[] segments = pattern.Split('*');
        int position = 0;

        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];
            if (segment.Length == 0)
            {
                continue;
            }

            if (index == 0)
            {
                if (!name.StartsWith(segment, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                position = segment.Length;
                continue;
            }

            if (index == segments.Length - 1 && !pattern.EndsWith('*'))
            {
                return name.Length - position >= segment.Length
                    && name.EndsWith(segment, StringComparison.OrdinalIgnoreCase);
            }

            int found = name.IndexOf(segment, position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            position = found + segment.Length;
        }

        return true;
    }

    private async Task<GitHubReleaseDto[]> GetReleasesAsync(
        string owner,
        string name,
        UpdateOptions options,
        CancellationToken cancellationToken)
    {
        HttpClient client = CreateClient(options);

        string url = $"{ApiBaseUrl}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}"
            + $"/releases?per_page={ReleasesToInspect}";

        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using Stream content = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        return await JsonSerializer
            .DeserializeAsync<GitHubReleaseDto[]>(content, cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? [];
    }

    private HttpClient CreateClient(UpdateOptions options)
    {
        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

        if (!string.IsNullOrWhiteSpace(options.Token))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.Token.Trim());
        }

        return client;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        string hint = response.StatusCode switch
        {
            // У приватного репозитория GitHub отвечает 404, а не 403: существование
            // репозитория он посторонним не подтверждает. Поэтому подсказка общая.
            HttpStatusCode.NotFound =>
                "Репозиторий или выпуск не найден. Если репозиторий приватный, нужен токен доступа",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "Токен не подошёл или исчерпан лимит обращений к GitHub",
            _ => $"GitHub ответил {(int)response.StatusCode}",
        };

        throw new HttpRequestException(
            $"{hint}. Ответ: {Shorten(body)}",
            null,
            response.StatusCode);
    }

    private async Task<bool> IsTooSoonAsync(UpdateOptions options, CancellationToken cancellationToken)
    {
        int hours = Math.Clamp(options.CheckIntervalHours, 0, 24 * 30);
        if (hours == 0)
        {
            return false;
        }

        string? raw = await _store
            .GetAsync(AppSettingKeys.UpdatesLastCheckAt, cancellationToken)
            .ConfigureAwait(false);

        return DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset last)
            && Time.GetUtcNow() - last < TimeSpan.FromHours(hours);
    }

    /// <summary>
    /// Разбирает значение вида <c>владелец/имя</c>.
    /// </summary>
    /// <remarks>
    /// Принимается и полный адрес страницы репозитория: его проще скопировать
    /// из браузера, чем набрать пару вручную.
    /// </remarks>
    internal static bool TryParseRepository(string? value, out string? owner, out string? name)
    {
        owner = null;
        name = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value.Trim();

        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
        {
            text = uri.AbsolutePath.Trim('/');
        }

        text = text.Trim('/');
        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^4];
        }

        string[] parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts.Any(part => part.Length == 0))
        {
            return false;
        }

        owner = parts[0];
        name = parts[1];
        return true;
    }

    /// <summary>
    /// Читает версию работающего приложения.
    /// </summary>
    /// <remarks>
    /// Берётся <c>AssemblyInformationalVersion</c>: там лежит значение из
    /// <c>-p:Version</c>, тогда как <c>AssemblyVersion</c> всегда четырёхчастная
    /// и без суффикса предварительного выпуска. Хвост после <c>+</c> с хэшем
    /// коммита разбор версии отбрасывает сам.
    /// </remarks>
    private static AppVersion ReadCurrentVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(GitHubUpdateService).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (AppVersion.TryParse(informational, out AppVersion? version))
        {
            return version;
        }

        return AppVersion.TryParse(assembly.GetName().Version?.ToString(), out AppVersion? fallback)
            ? fallback
            : AppVersion.Parse("0.0.0");
    }

    private static string Explain(Exception exception) => exception switch
    {
        TaskCanceledException => "GitHub не ответил вовремя",
        JsonException => "GitHub вернул неожиданный ответ",
        _ => exception.Message,
    };

    private static string Shorten(string value) =>
        value.Length <= 200 ? value.ReplaceLineEndings(" ") : value[..200].ReplaceLineEndings(" ") + "…";
}
