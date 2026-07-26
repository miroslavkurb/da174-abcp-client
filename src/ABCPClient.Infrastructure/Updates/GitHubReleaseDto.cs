using System.Text.Json.Serialization;

namespace ABCPClient.Infrastructure.Updates;

/// <summary>
/// Вложение выпуска GitHub.
/// </summary>
internal sealed class GitHubAssetDto
{
    /// <summary>Имя файла.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Размер в байтах.</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Адрес вложения в API.
    /// </summary>
    /// <remarks>
    /// Загрузка идёт именно по нему, а не по <c>browser_download_url</c>:
    /// у приватного репозитория прямая ссылка требует отдельной авторизации,
    /// а адрес API работает с тем же токеном. Для публичного репозитория
    /// он тоже открыт без токена, поэтому путь один для обоих случаев.
    /// </remarks>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>Прямая ссылка для человека.</summary>
    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}

/// <summary>
/// Выпуск GitHub.
/// </summary>
internal sealed class GitHubReleaseDto
{
    /// <summary>Тег выпуска.</summary>
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    /// <summary>Заголовок выпуска.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Заметки к выпуску.</summary>
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    /// <summary>Черновик: такие выпуски ещё не опубликованы.</summary>
    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    /// <summary>Предварительный выпуск.</summary>
    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    /// <summary>Когда опубликован.</summary>
    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Страница выпуска.</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    /// <summary>Вложения.</summary>
    [JsonPropertyName("assets")]
    public List<GitHubAssetDto> Assets { get; set; } = [];
}
