using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ABCPClient.Domain.Models;

/// <summary>
/// Версия приложения по правилам семантического версионирования.
/// </summary>
/// <remarks>
/// Своя реализация, а не <see cref="Version"/>, по двум причинам: <see cref="Version"/>
/// не понимает предварительные выпуски (<c>1.2.3-beta.1</c>) и сравнивает
/// <c>1.2.3</c> с <c>1.2.3.0</c> как разные значения. Для проверки обновлений
/// важно именно семантическое сравнение: предварительный выпуск старше того же
/// номера без суффикса, а метаданные сборки после <c>+</c> на порядок не влияют.
/// </remarks>
public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    private AppVersion(int major, int minor, int patch, string? prerelease, string display)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        Display = display;
    }

    /// <summary>Старшая часть.</summary>
    public int Major { get; }

    /// <summary>Средняя часть.</summary>
    public int Minor { get; }

    /// <summary>Младшая часть.</summary>
    public int Patch { get; }

    /// <summary>Суффикс предварительного выпуска без дефиса или <c>null</c>.</summary>
    public string? Prerelease { get; }

    /// <summary>Версия без метаданных сборки — то, что показывается пользователю.</summary>
    public string Display { get; }

    /// <summary>Это предварительный выпуск.</summary>
    public bool IsPrerelease => Prerelease is not null;

    /// <summary>
    /// Разбирает версию. Допускаются префикс <c>v</c>, четвёртая часть
    /// и метаданные сборки после <c>+</c>.
    /// </summary>
    /// <remarks>
    /// Четвёртая часть отбрасывается: .NET подставляет её в <c>FileVersion</c>
    /// (<c>1.0.0.0</c>), а в тегах и релизах её нет.
    /// </remarks>
    public static bool TryParse(string? value, [NotNullWhen(true)] out AppVersion? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        // Метаданные сборки на порядок версий не влияют: .NET дописывает туда
        // хэш коммита, и «1.0.0+abc» — это та же версия, что «1.0.0».
        int metadata = text.IndexOf('+', StringComparison.Ordinal);
        if (metadata >= 0)
        {
            text = text[..metadata];
        }

        string? prerelease = null;
        int dash = text.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];
            text = text[..dash];

            if (prerelease.Length == 0)
            {
                return false;
            }
        }

        string[] parts = text.Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        int[] numbers = new int[3];
        for (int index = 0; index < 3; index++)
        {
            if (index >= parts.Length)
            {
                numbers[index] = 0;
                continue;
            }

            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                return false;
            }

            numbers[index] = number;
        }

        string display = prerelease is null
            ? $"{numbers[0]}.{numbers[1]}.{numbers[2]}"
            : $"{numbers[0]}.{numbers[1]}.{numbers[2]}-{prerelease}";

        version = new AppVersion(numbers[0], numbers[1], numbers[2], prerelease, display);
        return true;
    }

    /// <summary>
    /// Разбирает версию или выбрасывает исключение.
    /// </summary>
    /// <exception cref="FormatException">Значение не является версией.</exception>
    public static AppVersion Parse(string value) =>
        TryParse(value, out AppVersion? version)
            ? version
            : throw new FormatException($"Не удалось разобрать версию: «{value}»");

    /// <inheritdoc />
    public int CompareTo(AppVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    /// <summary>
    /// Сравнивает суффиксы предварительных выпусков.
    /// </summary>
    /// <remarks>
    /// По правилам семантического версионирования выпуск без суффикса старше:
    /// <c>1.2.3</c> новее, чем <c>1.2.3-beta</c>. Внутри суффикса части
    /// сравниваются по точкам, причём числовые — как числа, чтобы
    /// <c>beta.10</c> оказалась новее <c>beta.9</c>.
    /// </remarks>
    private static int ComparePrerelease(string? left, string? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        string[] leftParts = left.Split('.');
        string[] rightParts = right.Split('.');

        for (int index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            if (index >= leftParts.Length)
            {
                return -1;
            }

            if (index >= rightParts.Length)
            {
                return 1;
            }

            bool leftIsNumber = int.TryParse(
                leftParts[index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int leftNumber);

            bool rightIsNumber = int.TryParse(
                rightParts[index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int rightNumber);

            int result = (leftIsNumber, rightIsNumber) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),

                // Числовая часть всегда младше буквенной.
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(leftParts[index], rightParts[index]),
            };

            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public bool Equals(AppVersion? other) => CompareTo(other) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AppVersion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    /// <inheritdoc />
    public override string ToString() => Display;

    /// <summary>Левая версия новее правой.</summary>
    public static bool operator >(AppVersion left, AppVersion right) => Compare(left, right) > 0;

    /// <summary>Левая версия старше правой.</summary>
    public static bool operator <(AppVersion left, AppVersion right) => Compare(left, right) < 0;

    /// <summary>Левая версия не старше правой.</summary>
    public static bool operator >=(AppVersion left, AppVersion right) => Compare(left, right) >= 0;

    /// <summary>Левая версия не новее правой.</summary>
    public static bool operator <=(AppVersion left, AppVersion right) => Compare(left, right) <= 0;

    /// <summary>Версии равны.</summary>
    public static bool operator ==(AppVersion? left, AppVersion? right) => Compare(left, right) == 0;

    /// <summary>Версии различаются.</summary>
    public static bool operator !=(AppVersion? left, AppVersion? right) => Compare(left, right) != 0;

    private static int Compare(AppVersion? left, AppVersion? right) =>
        left is null ? right is null ? 0 : -1 : left.CompareTo(right);
}
