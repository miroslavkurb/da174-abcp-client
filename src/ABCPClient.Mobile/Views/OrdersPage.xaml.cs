using ABCPClient.Mobile.ViewModels;

namespace ABCPClient.Mobile.Views;

/// <summary>
/// Экран списка заказов.
/// </summary>
public partial class OrdersPage : ContentPage
{
    private readonly OrdersViewModel _viewModel;

    /// <summary>Создаёт экран.</summary>
    /// <param name="viewModel">Модель представления списка заказов.</param>
    public OrdersPage(OrdersViewModel viewModel)
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
    /// Именно при появлении, а не один раз при создании: заказ могли изменить
    /// на другой вкладке или синхронизацией, и возврат должен показывать свежие данные.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
