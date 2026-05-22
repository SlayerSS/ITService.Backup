namespace ITService.Backup.Services;

public static class LockedFileCopier
{
    public static void CopyFile(string source, string destination, ShadowCopyService? vss)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (TryCopyDirect(source, destination))
            return;

        if (vss != null)
        {
            vss.TryCreateForPath(source);
            var shadowSource = vss.ResolveReadPath(source);
            if (TryCopyDirect(shadowSource, destination))
            {
                LogService.Info($"VSS: скопирован {Path.GetFileName(source)}");
                return;
            }
        }

        throw new IOException($"Не удалось скопировать файл (возможно занят): {source}");
    }

    private static bool TryCopyDirect(string source, string destination)
    {
        if (!File.Exists(source))
            return false;

        try
        {
            using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            src.CopyTo(dst);
            return true;
        }
        catch
        {
            try
            {
                File.Copy(source, destination, true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
