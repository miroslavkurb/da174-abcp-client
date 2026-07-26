using System.Globalization;
using ABCPClient.Mobile.ViewModels;

namespace ABCPClient.Mobile.Views;

/// <summary>
/// Экран состава задания на сборку.
/// </summary>
public partial class PickingTaskPage : ContentPage, IQueryAttributable
{
    private readonly PickingTaskViewModel _viewModel;

    /// <summary>Создаёт экран.</summary>
    /// <param name="viewModel">Модель представления задания.</param>
    public PickingTaskPage(PickingTaskViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.TryGetValue("id", out object? value)
            && int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
        {
            _ = _viewModel.LoadAsync(id);
        }
    }

    /// <summary>
    /// Возвращает фокус полю сканера.
    /// </summary>
    /// <remarks>
    /// Без фокуса аппаратный сканер печатает «в никуда»: он работает как
    /// клавиатура. Фокус возвращается и после каждой отметки, чтобы сборщик
    /// сканировал подряд, не касаясь экрана.
    /// </remarks>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        Dispatcher.Dispatch(() => ScanInput.Focus());
    }
}
