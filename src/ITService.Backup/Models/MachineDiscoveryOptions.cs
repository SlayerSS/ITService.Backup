namespace ITService.Backup.Models;

/// <summary>Параметры автоопределения SQL и путей 1С.</summary>
public sealed class MachineDiscoveryOptions
{
    /// <summary>Добавлять новые имена баз SQL в список настроек (первый запуск).</summary>
    public bool EnableNewSqlDatabases { get; init; } = true;

    /// <summary>Таймаут подключения при переборе локальных экземпляров (сек).</summary>
    public int LocalProbeTimeoutSeconds { get; init; } = 8;

    /// <summary>Таймаут подключения к уже указанному серверу (сек).</summary>
    public int ConfiguredConnectTimeoutSeconds { get; init; } = 8;
}
