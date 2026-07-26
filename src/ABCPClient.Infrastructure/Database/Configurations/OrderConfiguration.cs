using ABCPClient.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ABCPClient.Infrastructure.Database.Configurations;

/// <summary>
/// Описание таблицы заказов.
/// </summary>
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Orders");
        builder.HasKey(order => order.Id);

        // Онлайн-номер — стабильный ключ заказа в портале: по нему выполняется
        // сопоставление при синхронизации, поэтому нужен уникальный индекс.
        builder.Property(order => order.Number).IsRequired().HasMaxLength(64);
        builder.HasIndex(order => order.Number).IsUnique();

        builder.Property(order => order.InternalNumber).HasMaxLength(64);
        builder.Property(order => order.ClientOrderNumber).HasMaxLength(64);

        // Фильтр по номеру в учётной системе — основной сценарий обмена с 1С.
        builder.HasIndex(order => order.InternalNumber);

        builder.Property(order => order.UserName).HasMaxLength(256);
        builder.Property(order => order.UserFullName).HasMaxLength(256);
        builder.Property(order => order.UserEmail).HasMaxLength(256);
        builder.Property(order => order.UserMobile).HasMaxLength(64);
        builder.Property(order => order.UserCode).HasMaxLength(64);
        builder.Property(order => order.Comment).HasMaxLength(2000);
        builder.Property(order => order.DeliveryAddress).HasMaxLength(512);
        builder.Property(order => order.DeliveryOffice).HasMaxLength(256);
        builder.Property(order => order.DeliveryType).HasMaxLength(128);
        builder.Property(order => order.PaymentType).HasMaxLength(128);
        builder.Property(order => order.DominantStatusName).HasMaxLength(128);

        builder.Property(order => order.Sum).HasPrecision(18, 2);
        builder.Property(order => order.Debt).HasPrecision(18, 2);
        builder.Property(order => order.DeliveryCost).HasPrecision(18, 2);

        // Инкрементальная синхронизация и сортировка списка идут по дате обновления.
        builder.HasIndex(order => order.DateUpdated);
        builder.HasIndex(order => order.Date);
        builder.HasIndex(order => order.DominantStatusCode);

        builder.HasMany(order => order.Items)
            .WithOne(item => item.Order)
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
