using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace ABCPClient.UI.Services;

/// <summary>
/// Пишет ошибки привязок WPF в журнал приложения.
/// </summary>
/// <remarks>
/// По умолчанию WPF сообщает о проблемах привязки только в окно вывода отладчика,
/// поэтому в собранном приложении они остаются незамеченными до всплывающего окна
/// с исключением. Типичный пример: <c>Run.Text</c> привязан по умолчанию двусторонне
/// и не работает со свойством только для чтения.
/// </remarks>
public sealed class BindingErrorTraceListener : TraceListener
{
    private readonly ILogger _logger;

    private BindingErrorTraceListener(ILogger logger) => _logger = logger;

    /// <summary>
    /// Подключает перехват сообщений подсистемы привязок.
    /// </summary>
    /// <param name="logger">Журнал.</param>
    public static void Attach(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingErrorTraceListener(logger));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error | SourceLevels.Warning;
    }

    /// <inheritdoc />
    public override void Write(string? message)
    {
        // Сообщения приходят по частям: содержательным является WriteLine.
    }

    /// <inheritdoc />
    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning("Ошибка привязки WPF: {Message}", message);
        }
    }
}
