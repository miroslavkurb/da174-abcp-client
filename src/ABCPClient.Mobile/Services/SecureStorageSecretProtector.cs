using System.Security.Cryptography;
using System.Text;
using ABCPClient.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Mobile.Services;

/// <summary>
/// Шифрование секретов на Android: AES-GCM с ключом из защищённого хранилища.
/// </summary>
/// <remarks>
/// Замена Windows DPAPI, которого на Android нет. Ключ хранится в
/// <see cref="SecureStorage"/> — на Android это <c>EncryptedSharedPreferences</c>
/// поверх хранилища ключей системы, то есть ключ защищён самой платформой и
/// не лежит в базе рядом с шифротекстом.
/// AES-GCM, а не AES-CBC: он даёт проверку целостности, поэтому подменённое
/// или испорченное значение не расшифруется молча в мусор.
/// Ключ читается один раз при запуске (<see cref="InitializeAsync"/>), потому что
/// <see cref="ISecretProtector"/> синхронный, а хранилище — нет.
/// </remarks>
public sealed class SecureStorageSecretProtector : ISecretProtector
{
    /// <summary>Ключ записи в защищённом хранилище.</summary>
    private const string StorageKey = "ABCPClient.SecretKey.v1";

    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly ILogger<SecureStorageSecretProtector> _logger;

    private byte[]? _key;

    /// <summary>Создаёт протектор.</summary>
    public SecureStorageSecretProtector(ILogger<SecureStorageSecretProtector> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Читает ключ шифрования из защищённого хранилища, создавая его при первом запуске.
    /// </summary>
    /// <remarks>
    /// Вызывается один раз при старте приложения. Если хранилище недоступно
    /// (бывает на отдельных прошивках терминалов), протектор остаётся
    /// неинициализированным и обращения к нему завершатся исключением —
    /// это лучше, чем сохранить пароль API в базе открытым текстом.
    /// </remarks>
    public async Task InitializeAsync()
    {
        try
        {
            string? stored = await SecureStorage.GetAsync(StorageKey).ConfigureAwait(false);

            if (stored is { Length: > 0 } && Convert.TryFromBase64String(stored, new byte[KeySizeBytes], out int written)
                && written == KeySizeBytes)
            {
                _key = Convert.FromBase64String(stored);
                return;
            }

            byte[] created = RandomNumberGenerator.GetBytes(KeySizeBytes);
            await SecureStorage.SetAsync(StorageKey, Convert.ToBase64String(created)).ConfigureAwait(false);

            _key = created;
            _logger.LogInformation("Создан новый ключ шифрования секретов");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Защищённое хранилище недоступно, секреты сохранить не удастся");
        }
    }

    /// <inheritdoc />
    public string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        byte[] key = RequireKey();
        byte[] plain = Encoding.UTF8.GetBytes(plainText);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[TagSizeBytes];

        using AesGcm aes = new(key, TagSizeBytes);
        aes.Encrypt(nonce, plain, cipher, tag);

        // Одной строкой: соль, метка целостности и шифротекст подряд.
        byte[] result = new byte[NonceSizeBytes + TagSizeBytes + cipher.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSizeBytes);
        cipher.CopyTo(result, NonceSizeBytes + TagSizeBytes);

        return Convert.ToBase64String(result);
    }

    /// <inheritdoc />
    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        byte[] key = RequireKey();

        try
        {
            byte[] raw = Convert.FromBase64String(protectedValue);
            if (raw.Length <= NonceSizeBytes + TagSizeBytes)
            {
                return null;
            }

            byte[] nonce = raw[..NonceSizeBytes];
            byte[] tag = raw[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
            byte[] cipher = raw[(NonceSizeBytes + TagSizeBytes)..];
            byte[] plain = new byte[cipher.Length];

            using AesGcm aes = new(key, TagSizeBytes);
            aes.Decrypt(nonce, cipher, tag, plain);

            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            // Обычная причина: приложение переустановили, и ключ в хранилище сменился.
            // Значение секрета в журнал не попадает — только факт неудачи.
            _logger.LogWarning(
                exception,
                "Не удалось расшифровать сохранённый секрет. Потребуется ввести данные доступа заново");

            return null;
        }
    }

    private byte[] RequireKey() =>
        _key ?? throw new InvalidOperationException(
            "Ключ шифрования секретов не получен: защищённое хранилище недоступно");
}
