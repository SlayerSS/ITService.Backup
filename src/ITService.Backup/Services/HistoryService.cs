using System.Text.Json;
using System.Text.Json.Serialization;
using ITService.Backup.Models;

namespace ITService.Backup.Services;

public sealed class HistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BackupHistory Load()
    {
        AppPaths.EnsureDataDirectory();
        if (!File.Exists(AppPaths.HistoryFile))
            return new BackupHistory();

        var json = File.ReadAllText(AppPaths.HistoryFile);
        return JsonSerializer.Deserialize<BackupHistory>(json, JsonOptions) ?? new BackupHistory();
    }

    public void Save(BackupHistory history)
    {
        AppPaths.EnsureDataDirectory();
        var json = JsonSerializer.Serialize(history, JsonOptions);
        File.WriteAllText(AppPaths.HistoryFile, json);
    }

    public void RecordSuccess(string folder, long sizeBytes, IReadOnlyList<string> items)
    {
        var history = Load();
        var entry = new SuccessEntry
        {
            At = DateTime.Now,
            Folder = folder,
            SizeBytes = sizeBytes,
            Items = items.ToList()
        };
        history.LastSuccess = entry;
        history.LastAttempt = new AttemptEntry
        {
            At = entry.At,
            Success = true,
            Folder = folder,
            Items = items.ToList()
        };
        history.Recent.Insert(0, history.LastAttempt);
        if (history.Recent.Count > 20)
            history.Recent = history.Recent.Take(20).ToList();
        Save(history);
    }

    public void RecordFailure(string? error, IReadOnlyList<string>? items = null)
    {
        var history = Load();
        history.LastAttempt = new AttemptEntry
        {
            At = DateTime.Now,
            Success = false,
            Error = error,
            Items = items?.ToList() ?? []
        };
        history.Recent.Insert(0, history.LastAttempt);
        if (history.Recent.Count > 20)
            history.Recent = history.Recent.Take(20).ToList();
        Save(history);
    }
}
