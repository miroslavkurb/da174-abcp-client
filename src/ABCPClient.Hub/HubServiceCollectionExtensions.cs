using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ABCPClient.Hub;

/// <summary>
/// Регистрация узла склада.
/// </summary>
public static class HubServiceCollectionExtensions
{
    /// <summary>
    /// Добавляет узел склада: приём обращений терминалов сборки по локальной сети.
    /// </summary>
    /// <param name="services">Контейнер.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    public static IServiceCollection AddWarehouseHub(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<HubOptions>(configuration.GetSection(HubOptions.SectionName));

        // Один экземпляр: он хранит действующий код сопряжения в памяти.
        services.AddSingleton<DeviceRegistry>();

        // Узел регистрируется и как служба хоста, и сам по себе: интерфейсу нужны
        // его адреса и состояние, а второй экземпляр слушал бы тот же порт.
        services.AddSingleton<WarehouseHub>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<WarehouseHub>());

        return services;
    }
}
