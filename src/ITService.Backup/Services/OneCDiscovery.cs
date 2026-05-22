using ITService.Backup.Models;

namespace ITService.Backup.Services;

public static class OneCDiscovery
{
    public static bool TryFind1cd(string folderPath, out string? oneCdFilePath)
    {
        oneCdFilePath = null;
        if (!Directory.Exists(folderPath))
            return false;

        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(folderPath, "*.1cd", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return false;
        }
        if (candidates.Length == 0)
            return false;

        oneCdFilePath = candidates
            .OrderByDescending(f => new FileInfo(f).Length)
            .First();
        return true;
    }

    public static void ApplyToFolder(FolderEntry folder, bool defaultCopyOnlyBase = false)
    {
        folder.Has1CDatabase = TryFind1cd(folder.Path, out _);
        if (!folder.Has1CDatabase)
            folder.CopyOnly1cdBody = false;
        else if (defaultCopyOnlyBase)
            folder.CopyOnly1cdBody = true;
    }
}
