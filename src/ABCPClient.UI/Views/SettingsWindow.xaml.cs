using System.Windows;
using ABCPClient.UI.ViewModels;

namespace ABCPClient.UI.Views;

/// <summary>
/// Окно настроек подключения и синхронизации.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    /// <summary>
    /// Создаёт окно.
    /// </summary>
    /// <param name="viewModel">Модель представления настроек.</param>
    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        InitializeComponent();
        DataContext = viewModel;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.LoadCommand.ExecuteAsync(null);

    /// <summary>
    /// Переносит введённый пароль в модель представления.
    /// </summary>
    /// <remarks>
    /// Свойство <c>PasswordBox.Password</c> нельзя привязать напрямую: оно не является
    /// зависимым свойством — WPF намеренно не даёт держать пароль в разметке.
    /// </remarks>
    private void OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.Password = PasswordInput.Password;

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        await _viewModel.SaveCommand.ExecuteAsync(null);

        if (_viewModel.IsSaved)
        {
            PasswordInput.Clear();
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
