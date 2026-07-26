using ABCPClient.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ABCPClient.Infrastructure.Database.Configurations;

/// <summary>
/// Описание таблицы позиций заказов.
/// </summary>
public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("OrderItems");
        builder.HasKey(item => item.Id);

        // PositionId — стабильный идентификатор позиции в портале.
        // itemKey для этой роли не годится: он не уникален.
        builder.HasIndex(item => item.PositionId).IsUnique();

        builder.Property(item => item.Brand).IsRequired().HasMaxLength(128);
        builder.Property(item => item.BrandFix).HasMaxLength(128);
        builder.Property(item => item.Number).IsRequired().HasMaxLength(128);
        builder.Property(item => item.NumberFix).HasMaxLength(128);
        builder.Property(item => item.Description).HasMaxLength(1024);
        builder.Property(item => item.Status).HasMaxLength(128);
        builder.Property(item => item.DistributorName).HasMaxLength(256);
        builder.Property(item => item.DistributorOrderId).HasMaxLength(128);
        builder.Property(item => item.SupplierCode).HasMaxLength(128);
        builder.Property(item => item.ItemKey).HasMaxLength(256);
        builder.Property(item => item.Comment).HasMaxLength(2000);
        builder.Property(item => item.CommentAnswer).HasMaxLength(2000);

        builder.Property(item => item.Quantity).HasPrecision(18, 3);
        builder.Property(item => item.QuantityFinal).HasPrecision(18, 3);
        builder.Property(item => item.PriceIn).HasPrecision(18, 2);
        builder.Property(item => item.PriceOut).HasPrecision(18, 2);
        builder.Property(item => item.PriceInSiteCurrency).HasPrecision(18, 2);
        builder.Property(item => item.Weight).HasPrecision(18, 3);

        // Перечисления храним числами API: DistributorType 20/21/22,
        // CancelRequestState 0/1/2 — значения совпадают с протоколом.
        builder.Property(item => item.DistributorType).HasConversion<int>();
        builder.Property(item => item.CancelRequest).HasConversion<int>();
        builder.Property(item => item.CurrencyInId).HasConversion<int>();
        builder.Property(item => item.CurrencyOutId).HasConversion<int>();

        builder.HasIndex(item => item.StatusCode);
        builder.HasIndex(item => new { item.Brand, item.Number });

        builder.Ignore(item => item.Total);

        builder.HasMany(item => item.StatusHistory)
            .WithOne(entry => entry.OrderItem)
            .HasForeignKey(entry => entry.OrderItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
