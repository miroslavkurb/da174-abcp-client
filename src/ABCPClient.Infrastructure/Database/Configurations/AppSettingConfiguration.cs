using ABCPClient.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ABCPClient.Infrastructure.Database.Configurations;

/// <summary>
/// Описание таблицы настроек приложения.
/// </summary>
public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Settings");

        builder.HasKey(setting => setting.Key);
        builder.Property(setting => setting.Key).HasMaxLength(128);
        builder.Property(setting => setting.Value).HasMaxLength(4096);
    }
}
