using ABCPClient.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ABCPClient.Infrastructure.Database.Configurations;

/// <summary>
/// Описание таблицы справочника статусов.
/// </summary>
public sealed class OrderStatusConfiguration : IEntityTypeConfiguration<OrderStatus>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OrderStatus> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("OrderStatuses");

        // Ключ — код статуса из API; собственного локального идентификатора не нужно.
        builder.HasKey(status => status.StatusCode);
        builder.Property(status => status.StatusCode).ValueGeneratedNever();

        builder.Property(status => status.Name).IsRequired().HasMaxLength(128);
        builder.Property(status => status.Comment).HasMaxLength(1024);
        builder.Property(status => status.Color).HasMaxLength(32);
    }
}
