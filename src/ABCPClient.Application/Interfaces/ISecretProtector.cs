namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Шифрование секретов, сохраняемых на диск.
/// </summary>
/// <remarks>
/// md5-хэш пароля API — эквивалент пароля: его достаточно, чтобы обращаться к API
/// от имени API-администратора. Поэтому в базе он лежит только в зашифрованном виде.
/// </remarks>
public interface ISecretProtector
{
    /// <summary>
    /// Шифрует значение и возвращает шифротекст в base64.
    /// </summary>
    /// <param name="plainText">Открытое значение.</param>
    string Protect(string plainText);

    /// <summary>
    /// Расшифровывает значение, ранее полученное из <see cref="Protect"/>.
    /// </summary>
    /// <param name="protectedValue">Шифротекст в base64.</param>
    /// <returns>Открытое значение или <c>null</c>, если расшифровать не удалось.</returns>
    string? Unprotect(string? protectedValue);
}
