using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Application.Services;
using ABCPClient.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет опознание детали по вводу со сканера или с клавиатуры.
/// </summary>
public sealed class ArticleLookupServiceTests
{
    [Theory]
    [InlineData("4607030880082", true)]
    [InlineData("46070308", true)]
    [InlineData("4607030", false)]
    [InlineData("01089", false)]
    [InlineData("ADW-0855", false)]
    [InlineData("AMDBF561", false)]
    [InlineData("3182 654 213", false)]
    public void Barcode_is_told_apart_from_an_article(string value, bool expected) =>
        Assert.Equal(expected, ArticleLookupService.LooksLikeBarcode(value));

    [Theory]
    [InlineData("4607030880082\r\n", "4607030880082")]
    [InlineData("\t4607030880082\t", "4607030880082")]
    [InlineData("  ADW-0855  ", "ADW-0855")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Scanner_input_is_cleaned(string? input, string expected) =>
        Assert.Equal(expected, ArticleLookupService.Normalize(input));

    [Fact]
    public async Task Barcode_gives_an_exact_hit_without_searching()
    {
        FakeCards cards = new();
        cards.ByBarcode["4607030880082"] = Card("3Ton", "40036");

        ArticleLookupResult result = await Create(cards).LookupAsync("4607030880082\r\n");

        Assert.Equal(ArticleLookupKind.Barcode, result.Kind);
        Assert.True(result.LooksLikeBarcode);
        Assert.Equal("40036", result.Single!.Number);

        // Поиск не запускался: штрихкод дал точное совпадение.
        Assert.Empty(cards.Searches);
    }

    [Fact]
    public async Task Unknown_barcode_falls_back_to_search()
    {
        FakeCards cards = new();
        cards.SearchResults["4607030880082"] = [Card("3Ton", "40036")];

        ArticleLookupResult result = await Create(cards).LookupAsync("4607030880082");

        // Штрихкодов в кэше может не быть вовсе, поэтому поиск обязателен.
        Assert.Equal(ArticleLookupKind.Search, result.Kind);
        Assert.True(result.LooksLikeBarcode);
        Assert.Equal("4607030880082", Assert.Single(cards.Searches));
    }

    [Fact]
    public async Task Article_input_skips_the_barcode_lookup()
    {
        FakeCards cards = new();
        cards.SearchResults["ADW-0855"] = [Card("ACQ", "ADW0855")];

        ArticleLookupResult result = await Create(cards).LookupAsync("ADW-0855");

        Assert.Equal(ArticleLookupKind.Search, result.Kind);
        Assert.False(result.LooksLikeBarcode);

        // По штрихкоду не искали: артикул на него не похож.
        Assert.Empty(cards.BarcodeQueries);
    }

    [Fact]
    public async Task Several_matches_are_all_returned()
    {
        FakeCards cards = new();
        cards.SearchResults["втулка"] = [Card("Febi", "01089"), Card("Sasic", "4001772")];

        ArticleLookupResult result = await Create(cards).LookupAsync("втулка");

        Assert.Equal(2, result.Matches.Count);
        Assert.Null(result.Single);
        Assert.True(result.Found);
    }

    [Fact]
    public async Task Nothing_found_is_reported_as_such()
    {
        ArticleLookupResult result = await Create(new FakeCards()).LookupAsync("нетакого");

        Assert.Equal(ArticleLookupKind.NotFound, result.Kind);
        Assert.False(result.Found);
    }

    [Fact]
    public async Task Empty_input_costs_nothing()
    {
        FakeCards cards = new();

        ArticleLookupResult result = await Create(cards).LookupAsync("  \r\n ");

        Assert.Equal(ArticleLookupKind.Empty, result.Kind);
        Assert.Empty(cards.Searches);
        Assert.Empty(cards.BarcodeQueries);
    }

    private static ArticleLookupService Create(FakeCards cards) =>
        new(cards, NullLogger<ArticleLookupService>.Instance);

    private static ArticleCard Card(string brand, string number) => new()
    {
        Brand = brand,
        Number = number,
        Description = "Деталь",
        SyncedAt = DateTime.Now,
    };

    /// <summary>Кэш карточек, отдающий заготовленные ответы.</summary>
    private sealed class FakeCards : IArticleCardRepository
    {
        public Dictionary<string, ArticleCard> ByBarcode { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ArticleCard[]> SearchResults { get; } = new(StringComparer.Ordinal);

        public List<string> BarcodeQueries { get; } = [];

        public List<string> Searches { get; } = [];

        public Task<ArticleCard?> FindByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
        {
            BarcodeQueries.Add(barcode);

            return Task.FromResult(ByBarcode.TryGetValue(barcode, out ArticleCard? card) ? card : null);
        }

        public Task<IReadOnlyList<ArticleCard>> SearchAsync(
            string query,
            int limit = 50,
            CancellationToken cancellationToken = default)
        {
            Searches.Add(query);

            return Task.FromResult<IReadOnlyList<ArticleCard>>(
                SearchResults.TryGetValue(query, out ArticleCard[]? found) ? found : []);
        }

        public Task<IReadOnlyDictionary<string, ArticleCard>> GetAsync(
            IReadOnlyCollection<ArticleRef> articles,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, ArticleCard>>(
                new Dictionary<string, ArticleCard>(StringComparer.Ordinal));

        public Task UpsertAsync(IReadOnlyCollection<ArticleCard> cards, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
