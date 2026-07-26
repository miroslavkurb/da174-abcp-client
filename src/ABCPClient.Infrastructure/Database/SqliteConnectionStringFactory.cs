using ABCPClient.Application.Configuration;
using Microsoft.Data.Sqlite;

namespace ABCPClient.Infrastructure.Database;

/// <summary>
/// Строит строку подключения к файлу базы данных приложения.
/// </summary>
public static class SqliteConnectionStringFactory
{
    /// <summary>
    /// Возвращает строку подключения для файла базы из настроек.
    /// </summary>
    /// <param name="options">Настройки базы данных.</param>
    public static string Create(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Create(AppPaths.GetDatabasePath(options.FileName));
    }

    /// <summary>
    /// Возвращает строку подключения для указанного файла базы.
    /// </summary>
    /// <param name="databasePath">Полный путь к файлу базы.</param>
    public static string Create(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,

            // UI и фоновая служба открывают базу одновременно; Cache=Shared в паре
            // с журналом WAL позволяет читать во время записи.
            Cache = SqliteCacheMode.Shared,

            // Ожидание освобождения блокировки вместо мгновенного SQLITE_BUSY.
            DefaultTimeout = 30,

            ForeignKeys = true,
        };

        return builder.ToString();
    }
}
