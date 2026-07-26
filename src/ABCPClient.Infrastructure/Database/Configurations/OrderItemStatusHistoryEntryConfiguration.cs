using ABCPClient.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ABCPClient.Infrastructure.Database.Configurations;

/// <summary>
/// Описание таблицы истории статусов позиций.
/// </summary>
public sealed class OrderItemStatusHistoryEntryConfiguration
    : IEntityTypeConfiguration<OrderItemStatusHistoryEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OrderItemStatusHistoryEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("OrderItemStatusHistory");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Status).HasMaxLength(128);
        builder.Property(entry => entry.ManagerName).HasMaxLength(256);

        // API отдаёт историю пакетно и повторно; одна и та же смена статуса
        // не должна дублироваться в журнале.
        builder.HasIndex(entry => new { entry.OrderItemId, entry.StatusCode, entry.ChangedAt })
            .IsUnique();
    }
}
