namespace ITService.Backup.Models;

/// <summary>Один файл для резервного копирования (в т.ч. файловая база 1С).</summary>
public sealed class FileEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; }
    /// <summary>Файловая база 1С (.1CD).</summary>
    public bool Is1CDatabase { get; set; }
    /// <summary>Копировать только тело базы — файл .1CD, без служебных файлов каталога.</summary>
    public bool CopyOnly1cdBody { get; set; } = true;
}
