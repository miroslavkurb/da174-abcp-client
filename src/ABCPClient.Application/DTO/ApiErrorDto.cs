using System.Text.Json.Serialization;

namespace ABCPClient.Application.DTO;

/// <summary>
/// Тело ответа API при ошибке (любой HTTP-код 400 и выше).
/// </summary>
public sealed class ApiErrorDto
{
    /// <summary>Код ошибки API.</summary>
    [JsonPropertyName("errorCode")]
    public int ErrorCode { get; set; }

    /// <summary>Текстовое описание ошибки.</summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Коды ошибок API ABCP.
/// </summary>
public static class AbcpErrorCodes
{
    /// <summary>Ошибка синтаксиса запроса.</summary>
    public const int SyntaxError = 1;

    /// <summary>Не найден обязательный параметр запроса.</summary>
    public const int MissingRequiredParameter = 2;

    /// <summary>Неизвестная операция.</summary>
    public const int UnknownOperation = 3;

    /// <summary>Ошибка в параметре запроса.</summary>
    public const int InvalidParameter = 4;

    /// <summary>Неизвестная ошибка.</summary>
    public const int UnknownError = 13;

    /// <summary>Ошибка аутентификации пользователя.</summary>
    public const int UserAuthenticationError = 102;

    /// <summary>Доступ запрещён.</summary>
    public const int AccessDenied = 103;

    /// <summary>Ошибка аутентификации сайта.</summary>
    public const int SiteAuthenticationError = 104;

    /// <summary>Ошибка данных.</summary>
    public const int DataError = 201;

    /// <summary>Нарушение требования уникальности данных.</summary>
    public const int UniquenessViolation = 202;

    /// <summary>Объект не найден.</summary>
    public const int ObjectNotFound = 301;

    /// <summary>Ошибка кэша.</summary>
    public const int CacheError = 302;

    /// <summary>Ресурс заблокирован.</summary>
    public const int ResourceLocked = 303;

    /// <summary>
    /// Ошибки конфигурации и запроса: повторять такой вызов бессмысленно,
    /// нужно вмешательство пользователя или исправление кода.
    /// </summary>
    public static bool IsPermanent(int errorCode) => errorCode is
        SyntaxError or
        MissingRequiredParameter or
        UnknownOperation or
        InvalidParameter or
        UserAuthenticationError or
        AccessDenied or
        SiteAuthenticationError;
}
