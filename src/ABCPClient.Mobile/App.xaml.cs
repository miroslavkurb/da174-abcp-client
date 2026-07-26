using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Mobile;

/// <summary>
/// Приложение.
/// </summary>
/// <remarks>
/// Базовый тип указан полным именем: короткое <c>Application</c> совпадает
/// с именем нашего пространства имён <c>ABCPClient.Application</c>, и компилятор
/// выбирает пространство имён.
/// </remarks>
public partial class App : Microsoft.Maui.Controls.Application
{
    /// <summary>
    /// Создаёт приложение.
    /// </summary>
    /// <remarks>
    /// Оболочка запрашивается из контейнера в теле конструктора, а не приходит
    /// его параметром. Параметры вычисляются раньше тела, то есть страницы
    /// создавались бы до <c>InitializeComponent</c> — до того, как появятся
    /// ресурсы приложения, — и разбор разметки падал бы на первом же
    /// <c>StaticResource</c>.
    /// </remarks>
    /// <param name="services">Контейнер служб.</param>
    public App(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        InitializeComponent();

        try
        {
            MainPage = services.GetRequiredService<AppShell>();
        }
        catch (Exception exception)
        {
            // На Android исключение при запуске закрывает приложение молча:
            // ни пользователь, ни мы не узнаем причину. Поэтому вместо падения
            // показывается экран с текстом ошибки.
            services.GetService<ILogger<App>>()?.LogCritical(exception, "Не удалось построить интерфейс");

            MainPage = CreateFailurePage(exception);
        }
    }

    /// <summary>
    /// Экран с описанием ошибки запуска.
    /// </summary>
    private static Page CreateFailurePage(Exception exception) => new ContentPage
    {
        Title = "Ошибка запуска",
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 12,
                Children =
                {
                    new Label
                    {
                        Text = "Приложение не смогло запуститься",
                        FontSize = 20,
                        FontAttributes = FontAttributes.Bold,
                    },
                    new Label
                    {
                        Text = exception.GetType().Name,
                        FontSize = 15,
                        FontAttributes = FontAttributes.Bold,
                    },
                    new Label { Text = exception.Message, FontSize = 14 },
                    new Label
                    {
                        Text = exception.ToString(),
                        FontSize = 10,
                        TextColor = Colors.Gray,
                    },
                },
            },
        },
    };
}
