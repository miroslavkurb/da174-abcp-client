using ABCPClient.Application.Interfaces;
using ABCPClient.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ABCPClient.Application.DependencyInjection;

/// <summary>
/// Регистрация служб прикладного слоя.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Добавляет службы прикладного слоя (сценарии, доменные сервисы) в контейнер.
    /// </summary>
    /// <remarks>
    /// Слой не знает ни о конфигурации, ни об HTTP, ни о базе — только об интерфейсах
    /// из <c>ABCPClient.Application.Interfaces</c>. Их реализации подключает
    /// инфраструктурный слой.
    /// </remarks>
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISyncEventBus, SyncEventBus>();
        services.AddSingleton<IOrderSyncService, OrderSyncService>();

        // Singleton обязателен: служба ведёт учёт частоты обращений к API
        // и время остывания после ошибки 303.
        services.AddSingleton<IArticleCardService, ArticleCardService>();

        // Опознание детали по штрихкоду или артикулу: основа терминала сборки,
        // но пользуются им и настольная программа, и будущий узел склада.
        services.AddSingleton<IArticleLookup, ArticleLookupService>();

        return services;
    }
}
