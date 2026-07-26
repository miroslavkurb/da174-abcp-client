using ABCPClient.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ABCPClient.Infrastructure.Database.Configurations;

/// <summary>
/// Описание таблицы журнала синхронизации.
/// </summary>
public sealed class SyncLogEntryConfiguration : IEntityTypeConfiguration<SyncLogEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SyncLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SyncLog");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Operation).HasConversion<int>();
        builder.Property(entry => entry.Outcome).HasConversion<int>();
        builder.Property(entry => entry.Message).HasMaxLength(2000);

        builder.HasIndex(entry => entry.StartedAt);

        // Свойство вычисляемое, в базе не хранится.
        builder.Ignore(entry => entry.Duration);
    }
}
