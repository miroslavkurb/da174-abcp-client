using System.Security.Cryptography;
using System.Text;
using ABCPClient.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Infrastructure.Security;

/// <summary>
/// Шифрование секретов средствами Windows DPAPI.
/// </summary>
/// <remarks>
/// Используется область <see cref="DataProtectionScope.CurrentUser"/>: расшифровать
/// значение сможет только та же учётная запись Windows на той же машине.
/// Дополнительная энтропия привязывает шифротекст к приложению, поэтому чужие
/// DPAPI-данные этим протектором не расшифруются.
/// Перенос базы на другую машину или под другого пользователя потребует
/// повторного ввода пароля API — это осознанный компромисс в пользу безопасности.
/// </remarks>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ABCPClient.Secrets.v1");

    private readonly ILogger<DpapiSecretProtector> _logger;

    /// <summary>
    /// Создаёт протектор.
    /// </summary>
    /// <param name="logger">Журнал.</param>
    public DpapiSecretProtector(ILogger<DpapiSecretProtector> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        byte[] encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainText),
            Entropy,
            DataProtectionScope.CurrentUser);

        return Convert.ToBase64String(encrypted);
    }

    /// <inheritdoc />
    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            byte[] decrypted = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedValue),
                Entropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            // Типичные причины: базу скопировали с другой машины или из другого профиля.
            // Значение секрета в журнал не попадает — только факт неудачи.
            _logger.LogWarning(
                exception,
                "Не удалось расшифровать сохранённый секрет. Потребуется ввести данные доступа заново");
            return null;
        }
    }
}
