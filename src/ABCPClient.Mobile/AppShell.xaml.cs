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
    /// <param name="picking">Экран заданий на сборку.</param>
    /// <param name="orders">Экран списка заказов.</param>
    /// <param name="scan">Экран сканирования и поиска.</param>
    /// <param name="settings">Экран настроек.</param>
    public AppShell(PickingPage picking, OrdersPage orders, ScanPage scan, SettingsPage settings)
    {
        ArgumentNullException.ThrowIfNull(picking);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();

        PickingTab.Content = picking;
        OrdersTab.Content = orders;
        ScanTab.Content = scan;
        SettingsTab.Content = settings;

        // Экраны, открываемые переходом с параметром: их маршруты регистрируются
        // отдельно от вкладок.
        Routing.RegisterRoute(nameof(OrderDetailsPage), typeof(OrderDetailsPage));
        Routing.RegisterRoute(nameof(PickingTaskPage), typeof(PickingTaskPage));
    }
}
