using Microsoft.Extensions.Options;

namespace ABCPClient.Tests;

/// <summary>
/// Монитор параметров с фиксированным значением.
/// </summary>
/// <remarks>
/// Вынесен из отдельного набора тестов: параметры через <c>IOptionsMonitor</c>
/// принимают уже несколько служб, и копия на каждый набор тестов расходилась бы.
/// </remarks>
/// <typeparam name="T">Тип параметров.</typeparam>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    /// <summary>Создаёт монитор с постоянным значением.</summary>
    /// <param name="value">Значение параметров.</param>
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    /// <inheritdoc />
    public T CurrentValue { get; }

    /// <inheritdoc />
    public T Get(string? name) => CurrentValue;

    /// <inheritdoc />
    public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
