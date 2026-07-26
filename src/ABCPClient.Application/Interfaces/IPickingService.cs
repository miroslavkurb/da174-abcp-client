using ABCPClient.Application.DTO;
using ABCPClient.Domain.Entities;

namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Задания на сборку заказов.
/// </summary>
public interface IPickingService
{
    /// <summary>
    /// Создаёт задания на сборку по указанным заказам.
    /// </summary>
    /// <remarks>
    /// По одному заданию на заказ. Если по заказу уже есть незакрытое задание,
    /// новое не создаётся: два задания на один заказ означали бы, что товар
    /// соберут дважды.
    /// </remarks>
    /// <param name="orderNumbers">Онлайн-номера заказов ABCP.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<PickingTaskCreationResult> CreateTasksAsync(
        IReadOnlyCollection<string> orderNumbers,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает задания по фильтру.
    /// </summary>
    /// <param name="filter">Условия выборки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyList<PickingTaskListItem>> GetTasksAsync(
        PickingTaskFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает задание со строками.
    /// </summary>
    /// <param name="id">Локальный идентификатор задания.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<PickingTask?> GetTaskAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Фиксирует собранное количество по строке.
    /// </summary>
    /// <remarks>
    /// Идемпотентно: значение задаётся, а не прибавляется, поэтому повторная
    /// отправка с терминала при обрыве связи не удваивает факт.
    /// </remarks>
    /// <param name="request">Что и сколько собрано.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<PickingTask> RegisterPickAsync(PickRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Закрывает задание.
    /// </summary>
    /// <param name="id">Локальный идентификатор задания.</param>
    /// <param name="completedBy">Кто закрыл.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<PickingTask> CompleteTaskAsync(
        int id,
        string? completedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отменяет задание.
    /// </summary>
    /// <param name="id">Локальный идентификатор задания.</param>
    /// <param name="reason">Причина отмены.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<PickingTask> CancelTaskAsync(
        int id,
        string? reason,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Доступ к заданиям на сборку в локальной базе.
/// </summary>
public interface IPickingRepository
{
    /// <summary>Возвращает задания по фильтру.</summary>
    /// <param name="filter">Условия выборки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyList<PickingTask>> GetAsync(
        PickingTaskFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает задание со строками.</summary>
    /// <param name="id">Локальный идентификатор.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<PickingTask?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает номера заказов, по которым есть незакрытое задание.
    /// </summary>
    /// <param name="orderNumbers">Проверяемые номера заказов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlySet<string>> GetOrdersWithOpenTasksAsync(
        IReadOnlyCollection<string> orderNumbers,
        CancellationToken cancellationToken = default);

    /// <summary>Сохраняет новые задания.</summary>
    /// <param name="tasks">Задания.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task AddAsync(IReadOnlyCollection<PickingTask> tasks, CancellationToken cancellationToken = default);

    /// <summary>Сохраняет изменения задания.</summary>
    /// <param name="task">Задание, полученное из этого же хранилища.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task UpdateAsync(PickingTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает наибольший использованный порядковый номер задания.
    /// </summary>
    /// <param name="prefix">Префикс номера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<int> GetLastNumberAsync(string prefix, CancellationToken cancellationToken = default);
}
