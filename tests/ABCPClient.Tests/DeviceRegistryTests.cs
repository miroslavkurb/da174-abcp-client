using System.Net;
using ABCPClient.Application.Interfaces;
using ABCPClient.Hub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет учёт терминалов, подключённых к узлу склада.
/// </summary>
public sealed class DeviceRegistryTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-07-26T12:00:00Z", null);

    [Fact]
    public async Task Device_exchanges_the_code_for_a_token()
    {
        MutableTime time = new(Start);
        DeviceRegistry registry = Create(out MemoryStore store, time);

        string code = registry.IssuePairingCode();
        Assert.Equal(6, code.Length);
        Assert.Equal(code, registry.CurrentPairingCode);

        string? token = await registry.TryPairAsync(code, "ТСД-1");

        Assert.NotNull(token);
        Assert.Equal("ТСД-1", await registry.ResolveDeviceAsync(token));

        // В базе лежит только хэш: по её содержимому доступ получить нельзя.
        string stored = store.Values["Hub:Devices"]!;
        Assert.DoesNotContain(token, stored, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Code_works_only_once()
    {
        DeviceRegistry registry = Create(out _, new MutableTime(Start));

        string code = registry.IssuePairingCode();
        Assert.NotNull(await registry.TryPairAsync(code, "ТСД-1"));

        // Иначе подсмотренный код оставался бы годным до истечения срока.
        Assert.Null(await registry.TryPairAsync(code, "ТСД-2"));
        Assert.Null(registry.CurrentPairingCode);
    }

    [Fact]
    public async Task Expired_code_is_refused()
    {
        MutableTime time = new(Start);
        DeviceRegistry registry = Create(out _, time, new HubOptions { PairingCodeLifetimeMinutes = 10 });

        string code = registry.IssuePairingCode();

        time.Advance(TimeSpan.FromMinutes(11));

        Assert.Null(registry.CurrentPairingCode);
        Assert.Null(await registry.TryPairAsync(code, "ТСД-1"));
    }

    [Fact]
    public async Task Wrong_code_is_refused()
    {
        DeviceRegistry registry = Create(out _, new MutableTime(Start));

        registry.IssuePairingCode();

        Assert.Null(await registry.TryPairAsync("000000", "ТСД-1"));
        Assert.Null(await registry.TryPairAsync(null, "ТСД-1"));
        Assert.Null(await registry.TryPairAsync("   ", "ТСД-1"));
    }

    [Fact]
    public async Task Pairing_without_a_code_is_refused()
    {
        DeviceRegistry registry = Create(out _, new MutableTime(Start));

        // Кода не выдавали — подключаться нечем.
        Assert.Null(await registry.TryPairAsync("123456", "ТСД-1"));
    }

    [Fact]
    public async Task Unknown_token_resolves_to_nothing()
    {
        DeviceRegistry registry = Create(out _, new MutableTime(Start));

        Assert.Null(await registry.ResolveDeviceAsync("постороннийтокен"));
        Assert.Null(await registry.ResolveDeviceAsync(null));
        Assert.Null(await registry.ResolveDeviceAsync("  "));
    }

    [Fact]
    public async Task Reconnecting_the_same_device_replaces_its_token()
    {
        DeviceRegistry registry = Create(out _, new MutableTime(Start));

        string? first = await registry.TryPairAsync(registry.IssuePairingCode(), "ТСД-1");
        string? second = await registry.TryPairAsync(registry.IssuePairingCode(), "ТСД-1");

        // Иначе список копился бы после каждой переустановки приложения.
        Assert.Single(await registry.GetDevicesAsync());
        Assert.Equal("ТСД-1", await registry.ResolveDeviceAsync(second));
        Assert.Null(await registry.ResolveDeviceAsync(first));
    }

    [Fact]
    public async Task Revoked_device_loses_access()
    {
        DeviceRegistry registry = Create(out _, new MutableTime(Start));

        string? token = await registry.TryPairAsync(registry.IssuePairingCode(), "ТСД-1");

        Assert.True(await registry.RevokeDeviceAsync("тсд-1"));
        Assert.Null(await registry.ResolveDeviceAsync(token));
        Assert.False(await registry.RevokeDeviceAsync("тсд-1"));
    }

    [Fact]
    public async Task Nameless_device_gets_a_default_name()
    {
        DeviceRegistry registry = Create(out _, new MutableTime(Start));

        string? token = await registry.TryPairAsync(registry.IssuePairingCode(), "   ");

        Assert.Equal("Терминал", await registry.ResolveDeviceAsync(token));
    }

    [Fact]
    public async Task Corrupted_device_list_does_not_lock_the_hub()
    {
        MemoryStore store = new();
        store.Values["Hub:Devices"] = "не json";

        DeviceRegistry registry = new(
            store,
            new StaticOptionsMonitor<HubOptions>(new HubOptions()),
            NullLogger<DeviceRegistry>.Instance)
        {
            Time = new MutableTime(Start),
        };

        Assert.Empty(await registry.GetDevicesAsync());

        // Устройства должны иметь возможность подключиться заново.
        Assert.NotNull(await registry.TryPairAsync(registry.IssuePairingCode(), "ТСД-1"));
    }

    [Theory]
    [InlineData("192.168.0.103", true)]
    [InlineData("10.8.1.42", true)]
    [InlineData("172.16.5.1", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("203.0.113.7", false)]
    public void Private_addresses_are_told_apart(string address, bool expected) =>
        Assert.Equal(expected, WarehouseHub.IsPrivate(IPAddress.Parse(address)));

    private static DeviceRegistry Create(
        out MemoryStore store,
        MutableTime time,
        HubOptions? options = null)
    {
        store = new MemoryStore();

        return new DeviceRegistry(
            store,
            new StaticOptionsMonitor<HubOptions>(options ?? new HubOptions()),
            NullLogger<DeviceRegistry>.Instance)
        {
            Time = time,
        };
    }

    private sealed class MutableTime : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTime(DateTimeOffset now) => _utcNow = now.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow += delta;
    }

    private sealed class MemoryStore : IAppSettingsStore
    {
        public Dictionary<string, string?> Values { get; } = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.TryGetValue(key, out string? value) ? value : null);

        public Task SetAsync(
            string key,
            string? value,
            bool protect = false,
            CancellationToken cancellationToken = default)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string?>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string?>>(Values);

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.Remove(key));
    }
}
