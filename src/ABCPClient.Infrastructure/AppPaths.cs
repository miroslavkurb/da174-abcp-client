namespace ABCPClient.Infrastructure;

/// <summary>
/// Пути к пользовательским данным приложения.
/// </summary>
/// <remarks>
/// База и логи не должны лежать рядом с исполняемым файлом: при установке
/// в <c>Program Files</c> каталог недоступен на запись. Используется
/// <c>%LOCALAPPDATA%\ABCPClient</c>.
/// </remarks>
public static class AppPaths
{
    /// <summary>Имя каталога приложения внутри LocalApplicationData.</summary>
    private const string AppFolderName = "ABCPClient";

    /// <summary>Каталог данных приложения. Создаётся при первом обращении.</summary>
    public static string DataDirectory { get; } = EnsureDirectory(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName));

    /// <summary>Каталог журналов.</summary>
    public static string LogsDirectory { get; } = EnsureDirectory(Path.Combine(DataDirectory, "logs"));

    /// <summary>
    /// Каталог загруженных обновлений.
    /// </summary>
    /// <remarks>
    /// Не рядом с исполняемым файлом: он может лежать в <c>Program Files</c>,
    /// куда обычному пользователю писать нельзя.
    /// </remarks>
    public static string UpdatesDirectory { get; } = EnsureDirectory(Path.Combine(DataDirectory, "updates"));

    /// <summary>Полный путь к файлу базы данных.</summary>
    /// <param name="fileName">Имя файла базы из настроек.</param>
    public static string GetDatabasePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Path.Combine(DataDirectory, fileName);
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
