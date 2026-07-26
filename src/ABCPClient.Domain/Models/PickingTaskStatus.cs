namespace ABCPClient.Domain.Models;

/// <summary>
/// Состояние задания на сборку.
/// </summary>
public enum PickingTaskStatus
{
    /// <summary>Задание создано, сборка не начата.</summary>
    New = 0,

    /// <summary>Часть строк собрана.</summary>
    InProgress = 1,

    /// <summary>Собрано всё, что можно было собрать.</summary>
    Picked = 2,

    /// <summary>Задание отменено вручную.</summary>
    Cancelled = 3,
}
