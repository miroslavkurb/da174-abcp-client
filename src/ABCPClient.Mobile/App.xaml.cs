namespace ABCPClient.Mobile;

/// <summary>
/// Приложение.
/// </summary>
/// <remarks>
/// Базовый тип указан полным именем: короткое <c>Application</c> совпадает
/// с именем нашего пространства имён <c>ABCPClient.Application</c>, и компилятор
/// выбирает пространство имён.
/// </remarks>
public partial class App : Microsoft.Maui.Controls.Application
{
    /// <summary>Создаёт приложение.</summary>
    /// <param name="shell">Оболочка с вкладками, собранная контейнером.</param>
    public App(AppShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        InitializeComponent();

        MainPage = shell;
    }
}
