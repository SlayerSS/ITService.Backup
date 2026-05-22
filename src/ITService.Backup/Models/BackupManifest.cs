namespace ITService.Backup.Models;

public sealed class BackupManifest
{
    public DateTime CreatedAt { get; set; }
    public string Server { get; set; } = "";
    public string Vendor { get; set; } = "ITBackup";
    public string AppVersion { get; set; } = "1.0.0";
    public List<ManifestDatabase> Databases { get; set; } = [];
    public List<ManifestFolder> Folders { get; set; } = [];
    public List<ManifestFile> Files { get; set; } = [];
}

public sealed class ManifestDatabase
{
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public long SizeBytes { get; set; }
}

public sealed class ManifestFolder
{
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "";
    public string FileOrPath { get; set; } = "";
    public bool Has1CDatabase { get; set; }
    public bool CopyOnly1cdBody { get; set; }
    public long SizeBytes { get; set; }
}

public sealed class ManifestFile
{
    public string Name { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string File { get; set; } = "";
    public bool Is1CDatabase { get; set; }
    public bool CopyOnly1cdBody { get; set; }
    public long SizeBytes { get; set; }
}
