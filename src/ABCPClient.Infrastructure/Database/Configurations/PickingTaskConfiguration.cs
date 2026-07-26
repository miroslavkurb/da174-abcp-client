using ABCPClient.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ABCPClient.Infrastructure.Database.Configurations;

/// <summary>
/// Описание таблицы заданий на сборку.
/// </summary>
public sealed class PickingTaskConfiguration : IEntityTypeConfiguration<PickingTask>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PickingTask> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PickingTasks");
        builder.HasKey(task => task.Id);

        builder.Property(task => task.Number).IsRequired().HasMaxLength(32);
        builder.Property(task => task.OrderNumber).HasMaxLength(64);
        builder.Property(task => task.OneCOrderNumber).HasMaxLength(64);
        builder.Property(task => task.Customer).HasMaxLength(512);
        builder.Property(task => task.Warehouse).HasMaxLength(128);
        builder.Property(task => task.CompletedBy).HasMaxLength(128);
        builder.Property(task => task.Comment).HasMaxLength(2000);

        builder.Property(task => task.Status).HasConversion<int>();

        // Номер задания сквозной: по нему задание ищут и на терминале, и в журнале.
        builder.HasIndex(task => task.Number).IsUnique();

        // Повторное создание задания по тому же заказу — обычная ошибка оператора,
        // поэтому по номеру заказа нужен быстрый поиск существующих заданий.
        builder.HasIndex(task => task.OrderNumber);
        builder.HasIndex(task => task.Status);

        // Производные значения считаются по строкам и в базе не хранятся.
        builder.Ignore(task => task.InStockLines);
        builder.Ignore(task => task.IncomingLines);
        builder.Ignore(task => task.CompleteLines);

        builder.HasMany(task => task.Lines)
            .WithOne(line => line.Task)
            .HasForeignKey(line => line.PickingTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Описание таблицы строк заданий на сборку.
/// </summary>
public sealed class PickingTaskLineConfiguration : IEntityTypeConfiguration<PickingTaskLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PickingTaskLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PickingTaskLines");
        builder.HasKey(line => line.Id);

        builder.Property(line => line.Brand).IsRequired().HasMaxLength(128);
        builder.Property(line => line.Number).IsRequired().HasMaxLength(128);
        builder.Property(line => line.MatchKey).IsRequired().HasMaxLength(256);
        builder.Property(line => line.Description).HasMaxLength(1024);
        builder.Property(line => line.StockLocation).HasMaxLength(128);
        builder.Property(line => line.Barcodes).HasMaxLength(256);
        builder.Property(line => line.PickedBy).HasMaxLength(128);

        builder.Property(line => line.OrderedQuantity).HasPrecision(18, 3);
        builder.Property(line => line.AvailableQuantity).HasPrecision(18, 3);
        builder.Property(line => line.PickedQuantity).HasPrecision(18, 3);

        builder.Property(line => line.Availability).HasConversion<int>();

        // Поиск строки по сканеру идёт по ключу сопоставления.
        builder.HasIndex(line => line.MatchKey);

        // Позиция портала — связь со строкой заказа ABCP, если задание из него.
        builder.HasIndex(line => line.PositionId);

        builder.Ignore(line => line.IsComplete);
        builder.Ignore(line => line.IsStarted);
        builder.Ignore(line => line.Effective);
    }
}
