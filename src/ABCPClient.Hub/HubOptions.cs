namespace ABCPClient.Hub;

/// <summary>
/// Параметры узла склада — того, к чему подключаются терминалы.
/// </summary>
public sealed class HubOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Hub";

    /// <summary>Запускать узел вместе с программой.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Порт, на котором узел слушает.</summary>
    public int Port { get; set; } = 5080;

    /// <summary>
    /// Разрешать обращения из-за пределов локальной сети.
    /// </summary>
    /// <remarks>
    /// По умолчанию запрещено. Узел отдаёт состав заказов и принимает отметки
    /// о сборке, ему нечего делать в интернете; шифрования у него тоже нет.
    /// Если компьютер окажется доступен извне, ограничение по подсети — то,
    /// что удержит посторонних, даже когда токен утёк.
    /// </remarks>
    public bool AllowRemoteNetworks { get; set; }

    /// <summary>
    /// Сколько минут действует код сопряжения.
    /// </summary>
    /// <remarks>
    /// Код — короткий и произносимый вслух, поэтому он должен быстро
    /// становиться бесполезным.
    /// </remarks>
    public int PairingCodeLifetimeMinutes { get; set; } = 10;
}
