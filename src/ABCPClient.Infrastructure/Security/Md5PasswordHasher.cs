using System.Security.Cryptography;
using System.Text;
using ABCPClient.Application.Interfaces;

namespace ABCPClient.Infrastructure.Security;

/// <summary>
/// Преобразует пароль в значение параметра <c>userpsw</c> API ABCP.
/// </summary>
/// <remarks>
/// Алгоритм задан протоколом API: пароль передаётся как md5-хэш в шестнадцатеричном виде.
/// md5 здесь не является защитой пароля — это формат передачи, требуемый сервером,
/// поэтому полученный хэш хранится зашифрованным (см. <see cref="ISecretProtector"/>).
/// </remarks>
public sealed class Md5PasswordHasher : IPasswordHasher
{
    /// <summary>Длина md5-хэша в шестнадцатеричном виде.</summary>
    private const int HashLength = 32;

    /// <inheritdoc />
    public string ToApiHash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <inheritdoc />
    public bool LooksLikeHash(string? value)
    {
        if (value is null || value.Length != HashLength)
        {
            return false;
        }

        foreach (char symbol in value)
        {
            if (!Uri.IsHexDigit(symbol))
            {
                return false;
            }
        }

        return true;
    }
}
