using ABCPClient.Mobile.ViewModels;

namespace ABCPClient.Mobile.Views;

/// <summary>
/// Экран карточки заказа.
/// </summary>
/// <remarks>
/// Номер заказа приходит параметром перехода, поэтому экран реализует
/// <see cref="IQueryAttributable"/>: у Shell нет иного способа передать
/// значение странице, созданной контейнером.
/// </remarks>
public partial class OrderDetailsPage : ContentPage, IQueryAttributable
{
    private readonly OrderDetailsViewModel _viewModel;

    /// <summary>Создаёт экран.</summary>
    /// <param name="viewModel">Модель представления карточки заказа.</param>
    public OrderDetailsPage(OrderDetailsViewModel viewModel)
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

        if (!query.TryGetValue("number", out object? value) || value?.ToString() is not { Length: > 0 } number)
        {
            return;
        }

        // Загрузка запускается без ожидания: метод синхронный по контракту Shell,
        // а экран должен появиться сразу и заполниться по мере готовности.
        _ = _viewModel.LoadAsync(Uri.UnescapeDataString(number));
    }
}
