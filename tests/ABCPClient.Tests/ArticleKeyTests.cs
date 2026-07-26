using ABCPClient.Application.DTO;
using ABCPClient.Domain.Models;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет ключи детали.
/// </summary>
/// <remarks>
/// Примеры взяты из живых данных: слева написание в позиции заказа,
/// справа — в выгрузке каталога магазина.
/// </remarks>
public sealed class ArticleKeyTests
{
    [Theory]
    [InlineData("ACQ", "ADW-0855", "ACQ", "ADW0855")]
    [InlineData("Elring", "122.505", "Elring", "122505")]
    [InlineData("Filtron", "K 1378A", "Filtron", "K1378A")]
    [InlineData("Sachs", "3182 654 213", "Sachs", "3182654213")]
    [InlineData("Stellox", "150 1505-SX", "Stellox", "1501505SX")]
    public void Different_spellings_share_the_match_key(
        string orderBrand,
        string orderNumber,
        string catalogBrand,
        string catalogNumber)
    {
        Assert.Equal(
            ArticleKey.Match(catalogBrand, catalogNumber),
            ArticleKey.Match(orderBrand, orderNumber));

        // Точный ключ у таких написаний разный — ради него и появился сопоставительный.
        Assert.NotEqual(
            ArticleKey.Exact(catalogBrand, catalogNumber),
            ArticleKey.Exact(orderBrand, orderNumber));
    }

    [Fact]
    public void Different_articles_keep_different_keys()
    {
        Assert.NotEqual(ArticleKey.Match("Bosch", "0258006537"), ArticleKey.Match("Bosch", "0258006538"));
        Assert.NotEqual(ArticleKey.Match("Bosch", "0986452041"), ArticleKey.Match("Mann", "0986452041"));
    }

    [Fact]
    public void Article_ref_exposes_both_keys()
    {
        ArticleRef article = new("ACQ", "ADW-0855");

        Assert.Equal("acq|adw-0855", article.Key);
        Assert.Equal("acq|adw0855", article.MatchKey);
    }

    [Fact]
    public void Case_alone_is_already_handled_by_the_exact_key()
    {
        Assert.Equal(
            ArticleKey.Exact("Febi Bilstein", "01089"),
            ArticleKey.Exact("FEBI BILSTEIN", "01089"));
    }

    [Fact]
    public void Case_and_spaces_do_not_matter()
    {
        Assert.Equal(ArticleKey.Match(" acq ", " adw0855 "), ArticleKey.Match("ACQ", "ADW0855"));
        Assert.Equal(ArticleKey.Exact(" ACQ ", " ADW0855 "), ArticleKey.Exact("acq", "adw0855"));
    }
}
