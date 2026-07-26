namespace ABCPClient.Application.Configuration;

/// <summary>
/// Параметры локальной базы данных SQLite.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// Имя файла базы. Полный путь строится относительно каталога данных приложения
    /// (<c>%LOCALAPPDATA%\ABCPClient</c>), чтобы приложение работало и из Program Files.
    /// </summary>
    public string FileName { get; set; } = "abcpclient.db";
}
