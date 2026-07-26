using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ABCPClient.UI.ViewModels;

namespace ABCPClient.UI.Views;

/// <summary>
/// Карточка заказа: состав позиций с изображениями товаров.
/// </summary>
public partial class OrderDetailsWindow : Window
{
    private readonly OrderDetailsViewModel _viewModel;

    /// <summary>
    /// Создаёт окно.
    /// </summary>
    /// <param name="viewModel">Модель представления карточки заказа.</param>
    public OrderDetailsWindow(OrderDetailsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>
    /// Загружает заказ по номеру.
    /// </summary>
    /// <param name="number">Онлайн-номер заказа.</param>
    public Task LoadAsync(string number) => _viewModel.LoadAsync(number, CancellationToken.None);
}
