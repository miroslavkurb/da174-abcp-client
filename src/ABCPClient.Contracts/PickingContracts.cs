namespace ABCPClient.Contracts;

/// <summary>
/// Наличие детали для сборки в передаче по сети.
/// </summary>
/// <remarks>
/// Значения повторяют доменное перечисление, но живут отдельно: контракт между
/// узлом и терминалом не должен меняться каждый раз, когда меняется домен.
/// Передаётся строкой, а не числом, чтобы старый терминал не понял новое
/// значение как чужое.
/// </remarks>
public static class AvailabilityCodes
{
    /// <summary>Данных о наличии нет.</summary>
    public const string Unknown = "unknown";

    /// <summary>Есть на складе.</summary>
    public const string InStock = "in-stock";

    /// <summary>В пути на склад.</summary>
    public const string Incoming = "incoming";
}

/// <summary>
/// Состояние задания на сборку в передаче по сети.
/// </summary>
public static class PickingStatusCodes
{
    /// <summary>Сборка не начата.</summary>
    public const string New = "new";

    /// <summary>Часть строк собрана.</summary>
    public const string InProgress = "in-progress";

    /// <summary>Собрано всё доступное.</summary>
    public const string Picked = "picked";

    /// <summary>Задание отменено.</summary>
    public const string Cancelled = "cancelled";
}

/// <summary>
/// Задание на сборку в списке.
/// </summary>
/// <param name="Id">Идентификатор задания.</param>
/// <param name="Number">Номер задания.</param>
/// <param name="OrderNumber">Номер заказа ABCP.</param>
/// <param name="OneCOrderNumber">Номер заказа клиента в 1С.</param>
/// <param name="Customer">Клиент.</param>
/// <param name="Status">Состояние, см. <see cref="PickingStatusCodes"/>.</param>
/// <param name="CreatedAt">Когда создано.</param>
/// <param name="LinesCount">Всего строк.</param>
/// <param name="InStockLines">Строк в наличии.</param>
/// <param name="IncomingLines">Строк в пути.</param>
/// <param name="CompleteLines">Строк собрано полностью.</param>
public sealed record PickingTaskSummary(
    int Id,
    string Number,
    string? OrderNumber,
    string? OneCOrderNumber,
    string? Customer,
    string Status,
    DateTimeOffset CreatedAt,
    int LinesCount,
    int InStockLines,
    int IncomingLines,
    int CompleteLines);

/// <summary>
/// Строка задания на сборку.
/// </summary>
/// <param name="Id">Идентификатор строки.</param>
/// <param name="Brand">Бренд.</param>
/// <param name="Number">Артикул.</param>
/// <param name="Description">Наименование.</param>
/// <param name="OrderedQuantity">Сколько заказано.</param>
/// <param name="AvailableQuantity">Сколько доступно к сборке.</param>
/// <param name="PickedQuantity">Сколько собрано.</param>
/// <param name="Availability">Наличие, см. <see cref="AvailabilityCodes"/>.</param>
/// <param name="IncomingEta">Ожидаемое поступление.</param>
/// <param name="StockLocation">Место хранения.</param>
/// <param name="Barcodes">Штрихкоды.</param>
/// <param name="ImageName">Имя или адрес изображения товара.</param>
public sealed record PickingLine(
    int Id,
    string Brand,
    string Number,
    string? Description,
    decimal OrderedQuantity,
    decimal AvailableQuantity,
    decimal PickedQuantity,
    string Availability,
    DateTimeOffset? IncomingEta,
    string? StockLocation,
    IReadOnlyList<string> Barcodes,
    string? ImageName);

/// <summary>
/// Задание на сборку со строками.
/// </summary>
/// <param name="Task">Сведения о задании.</param>
/// <param name="Lines">Строки.</param>
public sealed record PickingTaskDetails(PickingTaskSummary Task, IReadOnlyList<PickingLine> Lines);

/// <summary>
/// Запрос на фиксацию собранного количества.
/// </summary>
/// <param name="Quantity">Собранное количество.</param>
/// <remarks>
/// Запрос идемпотентен: значение задаётся, а не прибавляется, поэтому повтор
/// при обрыве связи не удваивает факт.
/// </remarks>
public sealed record PickLineRequest(decimal Quantity);

/// <summary>
/// Запрос на закрытие задания.
/// </summary>
/// <param name="Comment">Комментарий сборщика.</param>
public sealed record CompleteTaskRequest(string? Comment);

/// <summary>
/// Запрос на подключение устройства.
/// </summary>
/// <param name="PairingCode">Код сопряжения, показанный в настольной программе.</param>
/// <param name="DeviceName">Имя устройства — попадёт в отметку о сборке.</param>
public sealed record DeviceAuthRequest(string PairingCode, string DeviceName);

/// <summary>
/// Ответ на подключение устройства.
/// </summary>
/// <param name="Token">Токен для последующих обращений.</param>
/// <param name="DeviceName">Принятое имя устройства.</param>
public sealed record DeviceAuthResponse(string Token, string DeviceName);

/// <summary>
/// Сведения об узле склада.
/// </summary>
/// <param name="Application">Имя приложения.</param>
/// <param name="Version">Версия.</param>
/// <param name="ServerTime">Время на узле.</param>
/// <param name="OpenTasks">Сколько незакрытых заданий.</param>
public sealed record HubInfo(string Application, string Version, DateTimeOffset ServerTime, int OpenTasks);

/// <summary>
/// Найденная деталь при поиске по штрихкоду или артикулу.
/// </summary>
/// <param name="Brand">Бренд.</param>
/// <param name="Number">Артикул.</param>
/// <param name="Description">Наименование.</param>
/// <param name="Barcodes">Штрихкоды.</param>
/// <param name="ImageName">Имя или адрес изображения.</param>
public sealed record ArticleMatch(
    string Brand,
    string Number,
    string? Description,
    IReadOnlyList<string> Barcodes,
    string? ImageName);

/// <summary>
/// Результат поиска детали.
/// </summary>
/// <param name="Kind">Как опознали: <c>barcode</c>, <c>search</c>, <c>not-found</c>.</param>
/// <param name="Query">Строка поиска после нормализации.</param>
/// <param name="Matches">Найденные детали.</param>
public sealed record ArticleLookupResponse(string Kind, string Query, IReadOnlyList<ArticleMatch> Matches);

/// <summary>
/// Ошибка узла в виде, пригодном для показа на терминале.
/// </summary>
/// <param name="Error">Текст ошибки.</param>
public sealed record HubError(string Error);
