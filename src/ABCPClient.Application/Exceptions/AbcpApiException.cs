using System.Net;
using ABCPClient.Application.DTO;

namespace ABCPClient.Application.Exceptions;

/// <summary>
/// Ошибка обращения к API ABCP.
/// </summary>
public sealed class AbcpApiException : Exception
{
    /// <summary>
    /// Создаёт исключение по ответу API.
    /// </summary>
    /// <param name="message">Сообщение.</param>
    /// <param name="statusCode">HTTP-код ответа.</param>
    /// <param name="errorCode">Код ошибки API (<c>errorCode</c>), если он был в теле ответа.</param>
    /// <param name="operation">Операция API, например <c>cp/orders</c>.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public AbcpApiException(
        string message,
        HttpStatusCode? statusCode = null,
        int? errorCode = null,
        string? operation = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Operation = operation;
    }

    /// <summary>HTTP-код ответа.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Код ошибки API.</summary>
    public int? ErrorCode { get; }

    /// <summary>Операция API, при вызове которой возникла ошибка.</summary>
    public string? Operation { get; }

    /// <summary>
    /// Ошибка не исправится повторным запросом: неверные реквизиты, нет прав,
    /// неизвестная операция или ошибка в параметрах.
    /// </summary>
    public bool IsPermanent => ErrorCode is { } code && AbcpErrorCodes.IsPermanent(code);

    /// <summary>
    /// Ошибка аутентификации или доступа — приложению нужно попросить проверить настройки.
    /// </summary>
    public bool IsAuthenticationFailure => ErrorCode is
        AbcpErrorCodes.UserAuthenticationError or
        AbcpErrorCodes.AccessDenied or
        AbcpErrorCodes.SiteAuthenticationError;
}

/// <summary>
/// Подключение к API не настроено: не заданы адрес, логин или пароль.
/// </summary>
public sealed class AbcpApiNotConfiguredException : Exception
{
    /// <summary>Создаёт исключение.</summary>
    public AbcpApiNotConfiguredException()
        : base("Подключение к API ABCP не настроено: укажите адрес, логин и пароль в настройках.")
    {
    }
}
