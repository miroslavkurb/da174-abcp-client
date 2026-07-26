using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ABCPClient.Application.DTO;
using ABCPClient.UI.ViewModels;

namespace ABCPClient.UI.Views;

/// <summary>
/// Главное окно приложения.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Func<SettingsWindow> _settingsWindowFactory;
    private readonly Func<OrderDetailsWindow> _orderDetailsWindowFactory;

    /// <summary>
    /// Создаёт окно.
    /// </summary>
    /// <param name="viewModel">Модель представления.</param>
    /// <param name="settingsWindowFactory">
    /// Фабрика окна настроек: окно создаётся контейнером при каждом открытии,
    /// чтобы поля заполнялись действующими настройками.
    /// </param>
    /// <param name="orderDetailsWindowFactory">Фабрика карточки заказа.</param>
    public MainWindow(
        MainViewModel viewModel,
        Func<SettingsWindow> settingsWindowFactory,
        Func<OrderDetailsWindow> orderDetailsWindowFactory)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(settingsWindowFactory);
        ArgumentNullException.ThrowIfNull(orderDetailsWindowFactory);

        _viewModel = viewModel;
        _settingsWindowFactory = settingsWindowFactory;
        _orderDetailsWindowFactory = orderDetailsWindowFactory;

        InitializeComponent();
        DataContext = viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.LoadCommand.ExecuteAsync(null);

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Journal.Dispose();
        _viewModel.Dispose();
    }

    /// <summary>
    /// Двойной клик по строке открывает карточку заказа.
    /// </summary>
    /// <remarks>
    /// Проверка попадания в строку обязательна: двойной клик по заголовку столбца
    /// или пустому месту таблицы не должен открывать окно.
    /// </remarks>
    private async void OnOrderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || FindParent<DataGridRow>(source) is not { Item: OrderListItem order })
        {
            return;
        }

        OrderDetailsWindow window = _orderDetailsWindowFactory();
        window.Owner = this;
        window.Show();

        await window.LoadAsync(order.Number);
    }

    private static T? FindParent<T>(DependencyObject source)
        where T : DependencyObject
    {
        DependencyObject? current = source;

        while (current is not null and not T)
        {
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return current as T;
    }

    private async void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        SettingsWindow window = _settingsWindowFactory();
        window.Owner = this;
        window.ShowDialog();

        // Настройки могли измениться: обновляем строку состояния, справочник и список.
        await _viewModel.RefreshConnectionStateAsync(CancellationToken.None);
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
