using ABCPClient.Application.DTO;

namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Опознание детали по вводу со сканера или с клавиатуры.
/// </summary>
/// <remarks>
/// Основа работы терминала сборки, но живёт в прикладном слое: тем же кодом
/// пользуются настольная программа и будущий серверный узел склада.
/// </remarks>
public interface IArticleLookup
{
    /// <summary>
    /// Опознаёт деталь по введённой строке.
    /// </summary>
    /// <param name="input">Штрихкод со сканера либо часть артикула, бренда, наименования.</param>
    /// <param name="limit">Сколько совпадений вернуть при поиске.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<ArticleLookupResult> LookupAsync(
        string input,
        int limit = 25,
        CancellationToken cancellationToken = default);
}
