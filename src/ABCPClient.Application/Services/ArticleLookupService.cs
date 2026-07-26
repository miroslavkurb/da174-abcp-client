using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Application.Services;

/// <summary>
/// Опознание детали по вводу со сканера или с клавиатуры.
/// </summary>
/// <remarks>
/// Аппаратные сканеры терминалов работают как клавиатура: подставляют строку
/// и жмут Enter, поэтому отдельной библиотеки для них не нужно — нужна
/// нормализация ввода и единая точка поиска.
/// Сначала пробуется штрихкод, затем поиск по артикулу. Порядок именно такой:
/// штрихкод даёт точное совпадение, а поиск — список. Но штрихкоды известны
/// не для всех деталей (в API ABCP их нет вовсе, только в выгрузке каталога),
/// поэтому поиск по артикулу обязателен, а не запасной вариант.
/// </remarks>
public sealed class ArticleLookupService : IArticleLookup
{
    /// <summary>Минимальная длина цифровой строки, чтобы считать её штрихкодом.</summary>
    /// <remarks>
    /// Короткие цифровые артикулы вроде <c>01089</c> штрихкодами не являются,
    /// а EAN-8 — самый короткий из применяемых форматов.
    /// </remarks>
    private const int MinimumBarcodeLength = 8;

    private readonly IArticleCardRepository _cards;
    private readonly ILogger<ArticleLookupService> _logger;

    /// <summary>Создаёт службу опознания.</summary>
    public ArticleLookupService(IArticleCardRepository cards, ILogger<ArticleLookupService> logger)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(logger);

        _cards = cards;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ArticleLookupResult> LookupAsync(
        string input,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        string normalized = Normalize(input);

        if (normalized.Length == 0)
        {
            return new ArticleLookupResult(ArticleLookupKind.Empty, string.Empty, false, []);
        }

        bool looksLikeBarcode = LooksLikeBarcode(normalized);

        if (looksLikeBarcode)
        {
            ArticleCard? byBarcode = await _cards
                .FindByBarcodeAsync(normalized, cancellationToken)
                .ConfigureAwait(false);

            if (byBarcode is not null)
            {
                return new ArticleLookupResult(
                    ArticleLookupKind.Barcode,
                    normalized,
                    true,
                    [byBarcode]);
            }

            _logger.LogDebug("Штрихкод {Barcode} в кэше карточек не найден", normalized);
        }

        IReadOnlyList<ArticleCard> found = await _cards
            .SearchAsync(normalized, limit, cancellationToken)
            .ConfigureAwait(false);

        return new ArticleLookupResult(
            found.Count > 0 ? ArticleLookupKind.Search : ArticleLookupKind.NotFound,
            normalized,
            looksLikeBarcode,
            found);
    }

    /// <summary>
    /// Чистит ввод сканера.
    /// </summary>
    /// <remarks>
    /// Сканеры дописывают перевод строки и табуляцию как разделитель, а часть
    /// моделей — управляющие символы префикса. В артикуле их быть не может,
    /// поэтому всё непечатаемое отбрасывается.
    /// </remarks>
    internal static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return string.Concat(input.Where(symbol => !char.IsControl(symbol))).Trim();
    }

    /// <summary>
    /// Похож ли ввод на штрихкод: только цифры и достаточная длина.
    /// </summary>
    internal static bool LooksLikeBarcode(string value) =>
        value.Length >= MinimumBarcodeLength && value.All(char.IsAsciiDigit);
}
