namespace ABCPClient.Application.Configuration;

/// <summary>
/// Параметры проверки обновлений через релизы GitHub.
/// </summary>
public sealed class UpdateOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Updates";

    /// <summary>Репозиторий в виде <c>владелец/имя</c>. Пусто — проверка выключена.</summary>
    /// <remarks>
    /// Значение по умолчанию задано в коде, а не только в <c>appsettings.json</c>:
    /// релиз раздаётся одним исполняемым файлом, рядом с которым файла настроек нет,
    /// и без этого обновления не работали бы «из коробки». Настройки приложения
    /// значение перекрывают.
    /// </remarks>
    public string Repository { get; set; } = "miroslavkurb/da174-abcp-client";

    /// <summary>
    /// Токен доступа к GitHub. Нужен только для приватного репозитория.
    /// </summary>
    /// <remarks>
    /// Хранится в базе зашифрованным через DPAPI, как и реквизиты API.
    /// В раздаваемый файл приложения токен не попадает и попадать не должен:
    /// он даёт доступ к исходному коду, а исполняемый файл может оказаться
    /// у кого угодно. Для публичного репозитория поле остаётся пустым.
    /// </remarks>
    public string Token { get; set; } = string.Empty;

    /// <summary>Проверять обновления при запуске.</summary>
    public bool CheckOnStartup { get; set; } = true;

    /// <summary>Учитывать предварительные выпуски.</summary>
    public bool IncludePrerelease { get; set; }

    /// <summary>
    /// Не проверять автоматически чаще, чем раз в столько часов.
    /// </summary>
    /// <remarks>
    /// У GitHub есть лимит обращений (60 в час без токена), а проверка при каждом
    /// запуске приложения на складе — это десятки обращений в день без всякой пользы.
    /// Проверка вручную ограничение не соблюдает.
    /// </remarks>
    public int CheckIntervalHours { get; set; } = 6;

    /// <summary>
    /// Маска имени файла обновления среди вложений релиза.
    /// </summary>
    /// <remarks>
    /// Совпадает с тем, как называет файлы рабочий процесс выпуска:
    /// <c>ABCPClient-1.0.0-win-x64.exe</c>.
    /// </remarks>
    public string AssetPattern { get; set; } = "*win-x64.exe";

    /// <summary>Имя вложения с контрольными суммами.</summary>
    public string ChecksumAssetName { get; set; } = "SHA256SUMS.txt";
}
