using ABCPClient.Mobile.ViewModels;

namespace ABCPClient.Mobile.Views;

/// <summary>
/// Экран сканирования и поиска детали.
/// </summary>
public partial class ScanPage : ContentPage
{
    /// <summary>Создаёт экран.</summary>
    /// <param name="viewModel">Модель представления поиска.</param>
    public ScanPage(ScanViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>
    /// Возвращает фокус полю ввода при появлении экрана.
    /// </summary>
    /// <remarks>
    /// Без фокуса аппаратный сканер отправляет штрихкод «в никуда»: он работает
    /// как клавиатура и печатает туда, где стоит курсор. Поэтому фокус ставится
    /// сразу, чтобы сборщик мог сканировать не касаясь экрана.
    /// </remarks>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        Dispatcher.Dispatch(() => ScanInput.Focus());
    }
}
