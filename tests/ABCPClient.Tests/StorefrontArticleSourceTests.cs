using ABCPClient.Application.DTO;
using ABCPClient.Infrastructure.Integration;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет разбор страницы товара на витрине магазина.
/// </summary>
/// <remarks>
/// Разметка взята с настоящей страницы da174.ru: платформа выводит изображение
/// и наименование в тегах Open Graph.
/// </remarks>
public sealed class StorefrontArticleSourceTests
{
    private const string Page = """
        <html><head>
        <title>Bosch 0258006537 Датчик кислорода, лямбда-зонд ВАЗ 1118</title>
        <meta property="og:url" content="https://da174.ru/parts/Bosch/0258006537" />
        <meta property="og:type" content="website" />
        <meta property="og:title" content="Bosch 0258006537 Датчик кислорода, лямбда-зонд ВАЗ 1118" />
        <meta property="og:description" content="Bosch 0258006537 Датчик кислорода, лямбда-зонд ВАЗ 1118" />
        <meta property="og:image" content="https://imgcdn.abcp.ru/p/full/09601d0c6e494a41f4d23633037b962b009c300002.jpeg" />
        </head><body></body></html>
        """;

    private const string EmptyPage = """
        <html><head>
        <meta property="og:url" content="https://da174.ru/parts/Bosch/ZZZNOSUCH" />
        <meta property="og:type" content="website" />
        <meta property="og:title" content="" />
        <meta property="og:description" content="" />
        </head><body></body></html>
        """;

    [Fact]
    public void Image_and_description_are_read_from_open_graph()
    {
        StorefrontArticle article = StorefrontArticleSource.Parse(
            Page,
            new ArticleRef("Bosch", "0258006537"));

        Assert.Equal(
            "https://imgcdn.abcp.ru/p/full/09601d0c6e494a41f4d23633037b962b009c300002.jpeg",
            article.ImageUrl);

        // Бренд и артикул из заголовка убираются: они уже есть в позиции заказа.
        Assert.Equal("Датчик кислорода, лямбда-зонд ВАЗ 1118", article.Description);
        Assert.False(article.IsEmpty);
    }

    [Fact]
    public void Page_without_product_is_reported_as_empty()
    {
        StorefrontArticle article = StorefrontArticleSource.Parse(
            EmptyPage,
            new ArticleRef("Bosch", "ZZZNOSUCH"));

        Assert.True(article.IsEmpty);
        Assert.Null(article.ImageUrl);
        Assert.Null(article.Description);
    }

    [Fact]
    public void Relative_or_foreign_image_address_is_ignored()
    {
        const string page = """
            <meta property="og:title" content="ACQ ADW-0855 Ремень" />
            <meta property="og:image" content="/images/local.jpeg" />
            """;

        StorefrontArticle article = StorefrontArticleSource.Parse(page, new ArticleRef("ACQ", "ADW-0855"));

        Assert.Null(article.ImageUrl);
        Assert.Equal("Ремень", article.Description);
    }

    [Fact]
    public void Html_entities_in_description_are_decoded()
    {
        const string page = """
            <meta property="og:title" content="Sachs 3182 654 213 Подшипник &quot;выжимной&quot; &amp; муфта" />
            """;

        StorefrontArticle article = StorefrontArticleSource.Parse(
            page,
            new ArticleRef("Sachs", "3182 654 213"));

        Assert.Equal("Подшипник \"выжимной\" & муфта", article.Description);
    }

    [Fact]
    public void Single_quoted_attributes_are_supported()
    {
        const string page = "<meta property='og:image' content='https://cdn.example/p.jpg'>";

        StorefrontArticle article = StorefrontArticleSource.Parse(page, new ArticleRef("A", "1"));

        Assert.Equal("https://cdn.example/p.jpg", article.ImageUrl);
    }
}
