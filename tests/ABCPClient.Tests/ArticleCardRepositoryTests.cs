using ABCPClient.Application.DTO;
using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;
using ABCPClient.Infrastructure.Database;
using ABCPClient.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет кэш карточек товаров: сопоставление артикулов между источниками
/// и поиск, на который опирается терминал сборки.
/// </summary>
public sealed class ArticleCardRepositoryTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"abcpclient-cards-{Guid.NewGuid():N}.db");

    private ArticleCardRepository _repository = null!;
    private TestFactory _factory = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        DbContextOptions<AbcpDbContext> options = new DbContextOptionsBuilder<AbcpDbContext>()
            .UseSqlite(SqliteConnectionStringFactory.Create(_databasePath))
            .Options;

        _factory = new TestFactory(options);
        _repository = new ArticleCardRepository(_factory);

        await using AbcpDbContext context = _factory.CreateDbContext();
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Article_written_differently_is_still_found()
    {
        // В каталоге артикул без разделителя, в заказе — с дефисом.
        await SaveAsync(Card("ACQ", "ADW0855", barcodes: null, description: "Клапан кондиционера"));

        IReadOnlyDictionary<string, ArticleCard> found = await _repository.GetAsync(
            [new ArticleRef("ACQ", "ADW-0855")]);

        ArticleCard card = found[new ArticleRef("ACQ", "ADW-0855").Key];
        Assert.Equal("ADW0855", card.Number);
        Assert.Equal("acq|adw0855", card.MatchKey);
    }

    [Fact]
    public async Task Exact_spelling_wins_over_normalized_one()
    {
        await SaveAsync(
            Card("Elring", "122505", description: "из каталога"),
            Card("Elring", "122.505", description: "как в заказе"));

        IReadOnlyDictionary<string, ArticleCard> found = await _repository.GetAsync(
            [new ArticleRef("Elring", "122.505")]);

        Assert.Equal("как в заказе", found.Values.Single().Description);
    }

    [Fact]
    public async Task Card_is_found_by_barcode()
    {
        await SaveAsync(
            Card("3Ton", "40036", barcodes: "4607030880082"),
            Card("Febi", "01089", barcodes: "4640562802795;1200000105074"));

        Assert.Equal("40036", (await _repository.FindByBarcodeAsync("4607030880082"))!.Number);

        // Второй штрихкод в списке ищется так же, как первый.
        Assert.Equal("01089", (await _repository.FindByBarcodeAsync("1200000105074"))!.Number);
    }

    [Fact]
    public async Task Partial_barcode_does_not_match()
    {
        await SaveAsync(Card("3Ton", "40036", barcodes: "4607030880082"));

        // Иначе сканирование короткого кода выдавало бы случайный товар.
        Assert.Null(await _repository.FindByBarcodeAsync("46070308"));
        Assert.Null(await _repository.FindByBarcodeAsync("0308800"));
    }

    [Fact]
    public async Task Barcode_search_ignores_empty_input_and_unknown_codes()
    {
        await SaveAsync(Card("3Ton", "40036", barcodes: "4607030880082"));

        Assert.Null(await _repository.FindByBarcodeAsync(string.Empty));
        Assert.Null(await _repository.FindByBarcodeAsync("   "));
        Assert.Null(await _repository.FindByBarcodeAsync("0000000000000"));
    }

    [Fact]
    public async Task Search_finds_by_article_brand_and_description()
    {
        await SaveAsync(
            Card("Bosch", "0258006537", description: "Датчик кислорода"),
            Card("Febi", "01089", description: "Опора двигателя"),
            Card("Sachs", "3182654213", description: "Подшипник выжимной"));

        Assert.Equal("0258006537", Assert.Single(await _repository.SearchAsync("0258006537")).Number);
        Assert.Equal("01089", Assert.Single(await _repository.SearchAsync("Febi")).Number);
        Assert.Equal("3182654213", Assert.Single(await _repository.SearchAsync("выжимной")).Number);
    }

    [Fact]
    public async Task Search_ignores_separators_in_the_article()
    {
        await SaveAsync(Card("Sachs", "3182654213", description: "Подшипник"));

        // На терминале артикул набирают как он написан на упаковке.
        Assert.Single(await _repository.SearchAsync("3182 654 213"));
        Assert.Single(await _repository.SearchAsync("3182-654-213"));
    }

    [Fact]
    public async Task Search_respects_the_limit_and_empty_query()
    {
        await SaveAsync(Enumerable.Range(1, 30)
            .Select(index => Card("Febi", $"0100{index}", description: "Втулка"))
            .ToArray());

        Assert.Equal(10, (await _repository.SearchAsync("Втулка", limit: 10)).Count);
        Assert.Empty(await _repository.SearchAsync("   "));
    }

    [Fact]
    public async Task Empty_values_do_not_overwrite_stored_ones()
    {
        await SaveAsync(Card("Febi", "01089", barcodes: "4640562802795", description: "Опора"));

        // Витрина отдаёт описание и картинку, но штрихкодов не знает.
        await SaveAsync(new ArticleCard
        {
            Brand = "Febi",
            Number = "01089",
            Description = "Опора двигателя",
            ImageName = "https://imgcdn.abcp.ru/p/full/aaa.jpeg",
            Source = ArticleCardSource.Storefront,
            SyncedAt = DateTime.Now,
        });

        ArticleCard card = Assert.Single(await _repository.SearchAsync("01089"));

        Assert.Equal("Опора двигателя", card.Description);
        Assert.Equal("https://imgcdn.abcp.ru/p/full/aaa.jpeg", card.ImageName);
        Assert.Equal("4640562802795", card.Barcodes);
        Assert.Equal(ArticleCardSource.Storefront, card.Source);
    }

    [Fact]
    public async Task Backfill_fills_match_key_for_old_rows()
    {
        // Карточка, записанная до появления ключа сопоставления.
        await using (AbcpDbContext context = _factory.CreateDbContext())
        {
            context.ArticleCards.Add(new ArticleCard
            {
                Brand = "ACQ",
                Number = "ADW-0855",
                MatchKey = string.Empty,
                SyncedAt = DateTime.Now,
            });

            await context.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Equal(1, await _repository.BackfillMatchKeysAsync());
        Assert.Equal(0, await _repository.BackfillMatchKeysAsync());

        IReadOnlyDictionary<string, ArticleCard> found = await _repository.GetAsync(
            [new ArticleRef("ACQ", "ADW0855")]);

        Assert.Single(found);
    }

    private static ArticleCard Card(
        string brand,
        string number,
        string? barcodes = null,
        string? description = null) => new()
    {
        Brand = brand,
        Number = number,
        Description = description,
        Barcodes = barcodes,
        Source = ArticleCardSource.Catalog,
        SyncedAt = DateTime.Now,
    };

    private Task SaveAsync(params ArticleCard[] cards) => _repository.UpsertAsync(cards);

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        foreach (string path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    private sealed class TestFactory : IDbContextFactory<AbcpDbContext>
    {
        private readonly DbContextOptions<AbcpDbContext> _options;

        public TestFactory(DbContextOptions<AbcpDbContext> options) => _options = options;

        public AbcpDbContext CreateDbContext() => new(_options);
    }
}
