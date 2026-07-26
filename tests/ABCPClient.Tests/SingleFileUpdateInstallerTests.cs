using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Models;
using ABCPClient.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет установку обновления подменой исполняемого файла.
/// </summary>
/// <remarks>
/// Сам перезапуск здесь не проверяется: тест не должен запускать процессы.
/// Проверяется то, что можно проверить, — определение пригодности, отказ
/// от непроверенного файла и удаление резервной копии.
/// </remarks>
public sealed class SingleFileUpdateInstallerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"abcpclient-install-{Guid.NewGuid():N}");

    public SingleFileUpdateInstallerTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Single_file_layout_can_be_updated()
    {
        string exe = CreateFile("ABCPClient.UI.exe", "старая версия");

        SingleFileUpdateInstaller installer = Create(exe);

        Assert.True(installer.CanInstall);
        Assert.Null(installer.UnavailableReason);
    }

    [Fact]
    public void Framework_dependent_layout_is_refused()
    {
        string exe = CreateFile("ABCPClient.UI.exe", "старая версия");
        CreateFile("ABCPClient.UI.dll", "рядом лежит сборка");

        SingleFileUpdateInstaller installer = Create(exe);

        Assert.False(installer.CanInstall);
        Assert.Contains("Desktop Runtime", installer.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_executable_is_refused()
    {
        SingleFileUpdateInstaller installer = Create(Path.Combine(_directory, "нет-такого.exe"));

        Assert.False(installer.CanInstall);
    }

    [Fact]
    public async Task Unverified_update_is_not_installed()
    {
        string exe = CreateFile("ABCPClient.UI.exe", "старая версия");
        string update = CreateFile("new.exe", "новая версия");

        SingleFileUpdateInstaller installer = Create(exe);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.InstallAndRestartAsync(
                new DownloadedUpdate(update, AppVersion.Parse("2.0.0"), ChecksumVerified: false)));

        Assert.Contains("Контрольная сумма", error.Message, StringComparison.Ordinal);

        // Установленная версия осталась на месте.
        Assert.Equal("старая версия", await File.ReadAllTextAsync(exe));
    }

    [Fact]
    public async Task Cleanup_removes_the_previous_version_and_clears_the_setting()
    {
        string backup = CreateFile("ABCPClient.UI.exe.old", "прошлая версия");

        MemoryStore store = new();
        store.Values[AppSettingKeys.UpdatesPendingCleanup] = backup;

        SingleFileUpdateInstaller installer = new(store, NullLogger<SingleFileUpdateInstaller>.Instance);

        await installer.CleanupAsync();

        Assert.False(File.Exists(backup));
        Assert.False(store.Values.ContainsKey(AppSettingKeys.UpdatesPendingCleanup));
    }

    [Fact]
    public async Task Cleanup_does_nothing_without_a_pending_file()
    {
        MemoryStore store = new();
        SingleFileUpdateInstaller installer = new(store, NullLogger<SingleFileUpdateInstaller>.Instance);

        await installer.CleanupAsync();

        Assert.Empty(store.Values);
    }

    [Fact]
    public async Task Cleanup_forgets_a_file_that_is_already_gone()
    {
        MemoryStore store = new();
        store.Values[AppSettingKeys.UpdatesPendingCleanup] = Path.Combine(_directory, "уже-удалён.old");

        SingleFileUpdateInstaller installer = new(store, NullLogger<SingleFileUpdateInstaller>.Instance);

        await installer.CleanupAsync();

        Assert.False(store.Values.ContainsKey(AppSettingKeys.UpdatesPendingCleanup));
    }

    private SingleFileUpdateInstaller Create(string executablePath) =>
        new(new MemoryStore(), NullLogger<SingleFileUpdateInstaller>.Instance)
        {
            ExecutablePath = executablePath,
        };

    private string CreateFile(string name, string content)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
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
