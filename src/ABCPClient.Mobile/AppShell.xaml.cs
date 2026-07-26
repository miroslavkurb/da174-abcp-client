using ABCPClient.Mobile.Views;

namespace ABCPClient.Mobile;

/// <summary>
/// Оболочка приложения: три вкладки и переход к карточке заказа.
/// </summary>
/// <remarks>
/// Страницы приходят из контейнера, а не создаются шаблоном разметки:
/// им нужны зависимости, а <c>ContentTemplate</c> создаёт объект напрямую.
/// </remarks>
public partial class AppShell : Shell
{
    /// <summary>Создаёт оболочку.</summary>
    /// <param name="orders">Экран списка заказов.</param>
    /// <param name="scan">Экран сканирования и поиска.</param>
    /// <param name="settings">Экран настроек.</param>
    public AppShell(OrdersPage orders, ScanPage scan, SettingsPage settings)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();

        OrdersTab.Content = orders;
        ScanTab.Content = scan;
        SettingsTab.Content = settings;

        // Карточка заказа открывается переходом с параметром, поэтому её маршрут
        // регистрируется отдельно от вкладок.
        Routing.RegisterRoute(nameof(OrderDetailsPage), typeof(OrderDetailsPage));
    }
}
