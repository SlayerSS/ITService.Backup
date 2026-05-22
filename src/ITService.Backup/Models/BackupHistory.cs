namespace ITService.Backup.Models;

public sealed class BackupHistory
{
    public SuccessEntry? LastSuccess { get; set; }
    public AttemptEntry? LastAttempt { get; set; }
    public List<AttemptEntry> Recent { get; set; } = [];
}

public sealed class SuccessEntry
{
    public DateTime At { get; set; }
    public string Folder { get; set; } = "";
    public long SizeBytes { get; set; }
    public List<string> Items { get; set; } = [];
}

public sealed class AttemptEntry
{
    public DateTime At { get; set; }
    public bool Success { get; set; }
    public string? Folder { get; set; }
    public string? Error { get; set; }
    public List<string> Items { get; set; } = [];
}
