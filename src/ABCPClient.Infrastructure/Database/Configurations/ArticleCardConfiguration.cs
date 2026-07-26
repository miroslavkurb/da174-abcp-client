using ABCPClient.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ABCPClient.Infrastructure.Database.Configurations;

/// <summary>
/// Описание таблицы кэша карточек товаров.
/// </summary>
public sealed class ArticleCardConfiguration : IEntityTypeConfiguration<ArticleCard>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ArticleCard> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ArticleCards");
        builder.HasKey(card => card.Id);

        builder.Property(card => card.Brand).IsRequired().HasMaxLength(128);
        builder.Property(card => card.Number).IsRequired().HasMaxLength(128);
        builder.Property(card => card.NumberFix).HasMaxLength(128);
        builder.Property(card => card.Description).HasMaxLength(1024);
        // Из выгрузки каталога приходит не имя файла, а полный адрес изображения.
        builder.Property(card => card.ImageName).HasMaxLength(512);
        builder.Property(card => card.PropertiesJson).HasMaxLength(8192);
        builder.Property(card => card.Barcodes).HasMaxLength(256);
        builder.Property(card => card.Source).HasConversion<int>();
        builder.Property(card => card.MatchKey).IsRequired().HasMaxLength(256).HasDefaultValue(string.Empty);

        // Деталь опознаётся парой «бренд + номер», поиск идёт только по ней.
        builder.HasIndex(card => new { card.Brand, card.Number }).IsUnique();

        // Основной путь поиска — сопоставительный ключ. Индекс не уникальный:
        // старые записи одного артикула в разных написаниях уже могли накопиться.
        builder.HasIndex(card => card.MatchKey);
    }
}
