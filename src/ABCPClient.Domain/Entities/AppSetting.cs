namespace ABCPClient.Domain.Entities;

/// <summary>
/// Пользовательская настройка приложения, сохранённая локально.
/// </summary>
/// <remarks>
/// Таблица построена как «ключ — значение», а не колонкой под каждый параметр:
/// набор настроек будет расширяться (интервалы, фильтры, параметры обмена с 1С),
/// и каждое добавление не должно требовать миграции схемы.
/// Значения, помеченные <see cref="IsProtected"/>, хранятся зашифрованными
/// средствами Windows DPAPI — так хранится md5-хэш пароля API.
/// </remarks>
public class AppSetting
{
    /// <summary>Ключ настройки, например <c>Abcp:PasswordMd5</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Значение настройки. Для защищённых настроек содержит шифротекст в base64,
    /// а не открытое значение.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>Значение зашифровано через DPAPI.</summary>
    public bool IsProtected { get; set; }

    /// <summary>Момент последнего изменения.</summary>
    public DateTime UpdatedAt { get; set; }
}
