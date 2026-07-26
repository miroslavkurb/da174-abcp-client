using System.Globalization;
using System.Text;

namespace ABCPClient.Infrastructure.Api;

/// <summary>
/// Строка запроса к API ABCP.
/// </summary>
/// <remarks>
/// Массивы передаются в стиле PHP: <c>positionsId[0]=1&amp;positionsId[1]=2</c>.
/// Даты форматируются как <c>ГГГГ-ММ-ДД ЧЧ:ММ:СС</c> — так их ждёт API.
/// Числа пишутся в инвариантной культуре, иначе на русской локали
/// разделителем дробной части стала бы запятая.
/// </remarks>
public sealed class AbcpQuery
{
    /// <summary>Имена параметров, значения которых нельзя писать в журнал.</summary>
    private static readonly HashSet<string> SecretParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "userpsw",
    };

    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";
    private const string MaskedValue = "***";

    private readonly List<KeyValuePair<string, string>> _parameters = [];

    /// <summary>Добавляет строковый параметр. Пустые значения пропускаются.</summary>
    public AbcpQuery Add(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!string.IsNullOrWhiteSpace(value))
        {
            _parameters.Add(new KeyValuePair<string, string>(name, value));
        }

        return this;
    }

    /// <summary>Добавляет целочисленный параметр.</summary>
    public AbcpQuery Add(string name, int? value) =>
        value is null ? this : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Добавляет логический параметр как <c>1</c> или <c>0</c>.</summary>
    public AbcpQuery Add(string name, bool? value) =>
        value is null ? this : Add(name, value.Value ? "1" : "0");

    /// <summary>Добавляет дату в формате API.</summary>
    public AbcpQuery Add(string name, DateTime? value) =>
        value is null ? this : Add(name, value.Value.ToString(DateFormat, CultureInfo.InvariantCulture));

    /// <summary>
    /// Добавляет массив значений в стиле PHP.
    /// </summary>
    /// <typeparam name="T">Тип элементов.</typeparam>
    /// <param name="name">Имя параметра без квадратных скобок.</param>
    /// <param name="values">Значения; <c>null</c> и пустая коллекция пропускаются.</param>
    public AbcpQuery AddArray<T>(string name, IEnumerable<T>? values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (values is null)
        {
            return this;
        }

        int index = 0;
        foreach (T value in values)
        {
            string? text = value switch
            {
                null => null,
                int number => number.ToString(CultureInfo.InvariantCulture),
                long number => number.ToString(CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                _parameters.Add(new KeyValuePair<string, string>(
                    string.Create(CultureInfo.InvariantCulture, $"{name}[{index}]"),
                    text));
                index++;
            }
        }

        return this;
    }

    /// <summary>
    /// Возвращает строку запроса.
    /// </summary>
    /// <param name="maskSecrets">
    /// Заменять значения секретных параметров на маску. Включать при записи в журнал:
    /// md5-хэш пароля равнозначен паролю и в логах оказываться не должен.
    /// </param>
    public string ToQueryString(bool maskSecrets = false)
    {
        StringBuilder builder = new();

        foreach ((string name, string value) in _parameters)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(name)).Append('=');

            if (maskSecrets && SecretParameters.Contains(name))
            {
                // Маску не экранируем: строка идёт в журнал и должна читаться глазами.
                builder.Append(MaskedValue);
            }
            else
            {
                builder.Append(Uri.EscapeDataString(value));
            }
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public override string ToString() => ToQueryString(maskSecrets: true);
}
