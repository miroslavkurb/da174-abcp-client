using ABCPClient.Mobile.ViewModels;

namespace ABCPClient.Mobile.Views;

/// <summary>
/// Экран списка заданий на сборку.
/// </summary>
public partial class PickingPage : ContentPage
{
    private readonly PickingViewModel _viewModel;

    /// <summary>Создаёт экран.</summary>
    /// <param name="viewModel">Модель представления заданий.</param>
    public PickingPage(PickingViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>
    /// Обновляет список при каждом появлении экрана.
    /// </summary>
    /// <remarks>
    /// Задания меняются на компьютере и на других терминалах, поэтому возврат
    /// с экрана задания должен показывать свежее состояние.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
