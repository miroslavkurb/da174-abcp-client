using System.Text;

namespace ABCPClient.Domain.Models;

/// <summary>
/// Ключи детали: точный и сопоставительный.
/// </summary>
/// <remarks>
/// Одна и та же деталь записывается по-разному в разных источниках:
/// в заказе <c>ADW-0855</c>, в каталоге <c>ADW0855</c>, у поставщика
/// <c>3182 654 213</c> против <c>3182654213</c>. Поэтому кроме точного ключа
/// есть сопоставительный: из бренда и номера убраны все знаки, кроме букв и цифр,
/// и регистр приведён к нижнему.
/// </remarks>
public static class ArticleKey
{
    /// <summary>
    /// Точный ключ: бренд и номер как есть, без учёта регистра и внешних пробелов.
    /// </summary>
    public static string Exact(string brand, string number) =>
        $"{brand.Trim().ToLowerInvariant()}|{number.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Сопоставительный ключ: только буквы и цифры в нижнем регистре.
    /// </summary>
    public static string Match(string brand, string number)
    {
        StringBuilder result = new(brand.Length + number.Length + 1);

        Append(result, brand);
        result.Append('|');
        Append(result, number);

        return result.ToString();
    }

    private static void Append(StringBuilder target, string value)
    {
        foreach (char symbol in value)
        {
            if (char.IsLetterOrDigit(symbol))
            {
                target.Append(char.ToLowerInvariant(symbol));
            }
        }
    }
}
