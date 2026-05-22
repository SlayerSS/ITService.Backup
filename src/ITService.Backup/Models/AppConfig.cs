namespace ITService.Backup.Models;

public sealed class AppConfig
{
    public string ProductName { get; set; } = "ITBackup";
    public UsbSettings Usb { get; set; } = new();
    public BackupTargetSettings BackupTarget { get; set; } = new();
    public string ArchiveNameFormat { get; set; } = "yyyy-MM-dd";
    public SqlServerSettings SqlServer { get; set; } = new();
    public List<FolderEntry> Folders { get; set; } = [];
    public List<FileEntry> Files { get; set; } = [];
    public UiSettings Ui { get; set; } = new();
}

public sealed class UsbSettings
{
    /// <summary>Автоматически выбирать диск на главном экране.</summary>
    public bool AutoDetectRemovable { get; set; } = true;

    /// <summary>Буква из списка в настройках (если не авто).</summary>
    public bool PickDriveManually { get; set; }

    /// <summary>Приоритет буквы при авто; при ручном выборе — обязательная буква. Пусто по умолчанию.</summary>
    public string DriveLetter { get; set; } = "";

    public string RootFolder { get; set; } = "Backups";
}

public sealed class SqlServerSettings
{
    /// <summary>Искать SQL на этом ПК; иначе используется <see cref="Instance"/> (IP или имя удалённого сервера).</summary>
    public bool UseLocalServer { get; set; } = true;
    public string Instance { get; set; } = "";
    public bool UseWindowsAuth { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public List<DatabaseEntry> Databases { get; set; } = [];
    /// <summary>Не включать файлы журнала транзакций (.ldf) в бэкап SQL.</summary>
    public bool ExcludeTransactionLogs { get; set; } = true;
    /// <summary>Сжатие BACKUP DATABASE (если поддерживается SQL Server).</summary>
    public bool UseCompression { get; set; } = true;
}

public sealed class DatabaseEntry
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; }
}

public sealed class FolderEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Archive { get; set; } = true;
    public string CompressionLevel { get; set; } = "optimal";
    /// <summary>В каталоге найден файл .1CD (файловая база 1С).</summary>
    public bool Has1CDatabase { get; set; }
    /// <summary>Копировать только тело базы (.1CD), без остальных файлов каталога.</summary>
    public bool CopyOnly1cdBody { get; set; }
}

public sealed class UiSettings
{
    /// <summary>Добавлять файловые базы 1С из ibases.v8i в список настроек.</summary>
    public bool AutoAddOneCDatabases { get; set; } = true;
    public bool RememberLastSelection { get; set; } = true;
    public string Warn1C { get; set; } = "Закройте 1С у всех пользователей перед копированием баз.";
    /// <summary>Первый авто-поиск SQL уже выполнялся.</summary>
    public bool InitialDiscoveryCompleted { get; set; }
}
