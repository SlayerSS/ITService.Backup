namespace ITService.Backup.Models;

public sealed class BackupProgressReport
{
    public int Percent { get; init; }
    public string CurrentAction { get; init; } = "";
    public IReadOnlyList<string> CompletedActions { get; init; } = [];
    public bool IsFinished { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }

    public static BackupProgressReport Create(int percent, string current, IEnumerable<string>? completed = null) =>
        new()
        {
            Percent = Math.Clamp(percent, 0, 100),
            CurrentAction = current,
            CompletedActions = completed?.ToList() ?? []
        };
}
