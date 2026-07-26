using System.Windows;
using ABCPClient.UI.ViewModels;

namespace ABCPClient.UI.Views;

/// <summary>
/// Окно проверки и установки обновлений.
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateViewModel _viewModel;

    /// <summary>
    /// Создаёт окно.
    /// </summary>
    /// <param name="viewModel">Модель представления обновлений.</param>
    public UpdateWindow(UpdateViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        InitializeComponent();
        DataContext = viewModel;

        Loaded += OnLoaded;
    }

    /// <summary>
    /// Проверяет обновления сразу при открытии.
    /// </summary>
    /// <remarks>
    /// Проверка принудительная: окно открыл человек, и ответ «проверяли недавно»
    /// ему бесполезен.
    /// </remarks>
    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.CheckAsync(force: true);

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
