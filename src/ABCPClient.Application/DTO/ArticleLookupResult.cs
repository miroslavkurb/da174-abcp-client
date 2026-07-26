using ABCPClient.Domain.Entities;

namespace ABCPClient.Application.DTO;

/// <summary>
/// Как деталь была опознана.
/// </summary>
public enum ArticleLookupKind
{
    /// <summary>Ввод пустой.</summary>
    Empty = 0,

    /// <summary>Найдено точное совпадение по штрихкоду.</summary>
    Barcode = 1,

    /// <summary>Найдено поиском по артикулу, бренду или наименованию.</summary>
    Search = 2,

    /// <summary>Ничего не найдено.</summary>
    NotFound = 3,
}

/// <summary>
/// Результат опознания детали.
/// </summary>
/// <param name="Kind">Как опознали.</param>
/// <param name="Input">Введённая строка после нормализации.</param>
/// <param name="LooksLikeBarcode">Ввод похож на штрихкод.</param>
/// <param name="Matches">Найденные карточки, самая точная первой.</param>
public sealed record ArticleLookupResult(
    ArticleLookupKind Kind,
    string Input,
    bool LooksLikeBarcode,
    IReadOnlyList<ArticleCard> Matches)
{
    /// <summary>Единственное совпадение — можно сразу его и использовать.</summary>
    public ArticleCard? Single => Matches.Count == 1 ? Matches[0] : null;

    /// <summary>Что-то нашлось.</summary>
    public bool Found => Matches.Count > 0;
}
