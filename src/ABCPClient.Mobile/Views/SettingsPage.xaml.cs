using ABCPClient.Mobile.ViewModels;

namespace ABCPClient.Mobile.Views;

/// <summary>
/// Экран настроек.
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly MobileSettingsViewModel _viewModel;

    /// <summary>Создаёт экран.</summary>
    /// <param name="viewModel">Модель представления настроек.</param>
    public SettingsPage(MobileSettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
