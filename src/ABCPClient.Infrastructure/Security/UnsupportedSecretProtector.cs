using ABCPClient.Application.Interfaces;

namespace ABCPClient.Infrastructure.Security;

/// <summary>
/// Заглушка шифрования секретов для платформ без собственной реализации.
/// </summary>
/// <remarks>
/// Регистрируется в сборке под платформонезависимую цель, где Windows DPAPI
/// недоступен. Намеренно выбрасывает исключение, а не хранит секреты открытым
/// текстом: молчаливое сохранение пароля API в читаемом виде — это утечка,
/// а не «работает и ладно». Платформа-хозяин обязана зарегистрировать свою
/// реализацию <see cref="ISecretProtector"/> после
/// <c>AddInfrastructureLayer</c> — она перекроет эту.
/// </remarks>
public sealed class UnsupportedSecretProtector : ISecretProtector
{
    private const string Message =
        "На этой платформе нет встроенного шифрования секретов. "
        + "Приложение должно зарегистрировать свою реализацию ISecretProtector "
        + "(например, поверх защищённого хранилища операционной системы).";

    /// <inheritdoc />
    public string Protect(string plainText) => throw new PlatformNotSupportedException(Message);

    /// <inheritdoc />
    public string? Unprotect(string? protectedValue) => throw new PlatformNotSupportedException(Message);
}
