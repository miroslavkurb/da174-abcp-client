namespace ABCPClient.Application.Configuration;

/// <summary>
/// Параметры подключения к API ABCP.
/// </summary>
/// <remarks>
/// Хост API индивидуален для каждого сайта и выдаётся менеджером ABCP,
/// поэтому <see cref="BaseUrl"/> обязателен и не имеет значения по умолчанию.
/// Пароль в API передаётся как md5-хэш (<see cref="PasswordMd5"/>), сам пароль приложению не нужен.
/// </remarks>
public sealed class AbcpApiOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Abcp";

    /// <summary>Базовый адрес API, например <c>https://demo.public.api.abcp.ru</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Логин пользователя со статусом «API-администратор».</summary>
    public string Login { get; set; } = string.Empty;

    /// <summary>
    /// md5-хэш пароля API-администратора (параметр <c>userpsw</c>).
    /// Хранится и передаётся вместо пароля, но является полноценным секретом доступа.
    /// </summary>
    public string PasswordMd5 { get; set; } = string.Empty;

    /// <summary>Таймаут одного HTTP-запроса, секунды.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Количество повторов при транспортных сбоях и ответах 5xx.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Размер страницы при постраничном чтении списков (параметр <c>limit</c>).
    /// Жёсткий предел ответа сервера — 1000 записей.
    /// </summary>
    public int PageSize { get; set; } = 500;

    /// <summary>Максимально допустимое значение <c>limit</c> на стороне API.</summary>
    public const int MaxPageSize = 1000;

    /// <summary>
    /// Признак заполненности обязательных настроек. Пока он <c>false</c>,
    /// обращаться к API нельзя: приложение должно предложить открыть окно настроек.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Login)
        && !string.IsNullOrWhiteSpace(PasswordMd5);
}
