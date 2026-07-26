using System.Reflection;
using ABCPClient.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ABCPClient.Infrastructure.Database;

/// <summary>
/// Контекст локальной базы данных приложения (SQLite).
/// </summary>
/// <remarks>
/// Контекст создаётся через <see cref="IDbContextFactory{TContext}"/>, а не внедряется
/// как scoped-зависимость: фоновая синхронизация и UI работают параллельно,
/// а экземпляр <see cref="DbContext"/> не потокобезопасен.
/// Наборы сущностей (<c>DbSet</c>) и их конфигурации добавляются на этапе моделей.
/// </remarks>
public class AbcpDbContext : DbContext
{
    /// <summary>
    /// Создаёт контекст с переданными параметрами подключения.
    /// </summary>
    /// <param name="options">Параметры контекста.</param>
    public AbcpDbContext(DbContextOptions<AbcpDbContext> options)
        : base(options)
    {
    }

    /// <summary>Заказы.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>Позиции заказов.</summary>
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    /// <summary>История изменения статусов позиций.</summary>
    public DbSet<OrderItemStatusHistoryEntry> OrderItemStatusHistory => Set<OrderItemStatusHistoryEntry>();

    /// <summary>Справочник статусов позиций заказов.</summary>
    public DbSet<OrderStatus> OrderStatuses => Set<OrderStatus>();

    /// <summary>Журнал синхронизации.</summary>
    public DbSet<SyncLogEntry> SyncLog => Set<SyncLogEntry>();

    /// <summary>Настройки приложения.</summary>
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    /// <summary>Кэш карточек товаров.</summary>
    public DbSet<ArticleCard> ArticleCards => Set<ArticleCard>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Конфигурации сущностей (IEntityTypeConfiguration) подхватываются автоматически,
        // чтобы описание таблиц не копилось в одном методе.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        base.OnModelCreating(modelBuilder);
    }
}
