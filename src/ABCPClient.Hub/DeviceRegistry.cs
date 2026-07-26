using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ABCPClient.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ABCPClient.Hub;

/// <summary>
/// Учёт терминалов, подключённых к узлу склада.
/// </summary>
/// <remarks>
/// Подключение идёт в два шага: в настольной программе показывается короткий код
/// сопряжения, терминал обменивает его на постоянный токен. Так пароль руками
/// на телефоне не набирают, а код быстро перестаёт действовать.
/// В базе хранится только хэш токена: файл базы лежит на обычном компьютере,
/// и по её содержимому получить доступ к узлу не должно быть возможно.
/// </remarks>
public sealed class DeviceRegistry
{
    private const string StorageKey = "Hub:Devices";

    private readonly IAppSettingsStore _store;
    private readonly IOptionsMonitor<HubOptions> _options;
    private readonly ILogger<DeviceRegistry> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _pairingCode;
    private DateTimeOffset _pairingCodeExpiresAt;

    /// <summary>Создаёт учёт устройств.</summary>
    public DeviceRegistry(
        IAppSettingsStore store,
        IOptionsMonitor<HubOptions> options,
        ILogger<DeviceRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _options = options;
        _logger = logger;
    }

    /// <summary>Источник времени. Отдельным свойством — ради предсказуемости тестов.</summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Действующий код сопряжения или <c>null</c>, если он не выдан или истёк.</summary>
    public string? CurrentPairingCode =>
        _pairingCode is not null && Time.GetUtcNow() < _pairingCodeExpiresAt ? _pairingCode : null;

    /// <summary>До какого момента действует код сопряжения.</summary>
    public DateTimeOffset PairingCodeExpiresAt => _pairingCodeExpiresAt;

    /// <summary>
    /// Выдаёт новый код сопряжения, отменяя прежний.
    /// </summary>
    public string IssuePairingCode()
    {
        // Шесть цифр: код читают с экрана и набирают на терминале. Стойкость
        // обеспечивает не длина, а срок жизни и то, что подключение идёт
        // только из локальной сети.
        string code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", null);

        _pairingCode = code;
        _pairingCodeExpiresAt = Time.GetUtcNow()
            + TimeSpan.FromMinutes(Math.Clamp(_options.CurrentValue.PairingCodeLifetimeMinutes, 1, 120));

        _logger.LogInformation("Выдан код сопряжения, действует до {ExpiresAt:HH:mm:ss}", _pairingCodeExpiresAt);

        return code;
    }

    /// <summary>Отменяет действующий код сопряжения.</summary>
    public void RevokePairingCode()
    {
        _pairingCode = null;
        _pairingCodeExpiresAt = default;
    }

    /// <summary>
    /// Обменивает код сопряжения на токен устройства.
    /// </summary>
    /// <param name="pairingCode">Код, показанный в настольной программе.</param>
    /// <param name="deviceName">Имя устройства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Токен или <c>null</c>, если код неверен или истёк.</returns>
    public async Task<string?> TryPairAsync(
        string? pairingCode,
        string? deviceName,
        CancellationToken cancellationToken = default)
    {
        string? expected = CurrentPairingCode;
        if (expected is null || string.IsNullOrWhiteSpace(pairingCode))
        {
            return null;
        }

        // Сравнение постоянного времени: код короткий, и подбирать его по времени
        // ответа не должно быть проще, чем наугад.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(pairingCode.Trim())))
        {
            _logger.LogWarning("Попытка подключения устройства с неверным кодом сопряжения");
            return null;
        }

        string name = string.IsNullOrWhiteSpace(deviceName) ? "Терминал" : deviceName.Trim();
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<DeviceRecord> devices = await LoadAsync(cancellationToken).ConfigureAwait(false);

            // Повторное подключение того же устройства заменяет прежний токен:
            // иначе список копился бы после каждой переустановки приложения.
            devices.RemoveAll(device => string.Equals(device.Name, name, StringComparison.OrdinalIgnoreCase));

            devices.Add(new DeviceRecord
            {
                Name = name,
                TokenHash = Hash(token),
                PairedAt = Time.GetUtcNow(),
            });

            await SaveAsync(devices, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        // Код одноразовый: сопряжение состоялось, и держать его действующим незачем.
        RevokePairingCode();

        _logger.LogInformation("Устройство «{Device}» подключено к узлу", name);

        return token;
    }

    /// <summary>
    /// Возвращает имя устройства по токену или <c>null</c>, если токен неизвестен.
    /// </summary>
    /// <param name="token">Токен из заголовка запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<string?> ResolveDeviceAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string hash = Hash(token.Trim());
        List<DeviceRecord> devices = await LoadAsync(cancellationToken).ConfigureAwait(false);

        return devices.FirstOrDefault(device =>
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(device.TokenHash),
                Encoding.UTF8.GetBytes(hash)))?.Name;
    }

    /// <summary>Возвращает подключённые устройства.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<IReadOnlyList<(string Name, DateTimeOffset PairedAt)>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        List<DeviceRecord> devices = await LoadAsync(cancellationToken).ConfigureAwait(false);

        return devices.Select(device => (device.Name, device.PairedAt)).ToArray();
    }

    /// <summary>Отключает устройство.</summary>
    /// <param name="name">Имя устройства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c>, если устройство было подключено.</returns>
    public async Task<bool> RevokeDeviceAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<DeviceRecord> devices = await LoadAsync(cancellationToken).ConfigureAwait(false);

            if (devices.RemoveAll(device =>
                    string.Equals(device.Name, name, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                return false;
            }

            await SaveAsync(devices, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Устройство «{Device}» отключено от узла", name);

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private async Task<List<DeviceRecord>> LoadAsync(CancellationToken cancellationToken)
    {
        string? raw = await _store.GetAsync(StorageKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<DeviceRecord>>(raw) ?? [];
        }
        catch (JsonException)
        {
            // Испорченное значение не должно закрывать доступ навсегда:
            // устройства подключатся заново.
            _logger.LogWarning("Список устройств узла испорчен и будет создан заново");
            return [];
        }
    }

    private Task SaveAsync(List<DeviceRecord> devices, CancellationToken cancellationToken) =>
        _store.SetAsync(StorageKey, JsonSerializer.Serialize(devices), cancellationToken: cancellationToken);

    /// <summary>Запись о подключённом устройстве.</summary>
    private sealed class DeviceRecord
    {
        /// <summary>Имя устройства.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Хэш токена: сам токен не хранится.</summary>
        [JsonPropertyName("tokenHash")]
        public string TokenHash { get; set; } = string.Empty;

        /// <summary>Когда устройство подключили.</summary>
        [JsonPropertyName("pairedAt")]
        public DateTimeOffset PairedAt { get; set; }
    }
}
