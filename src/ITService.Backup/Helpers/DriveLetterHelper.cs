namespace ITService.Backup.Helpers;

/// <summary>Единая нормализация буквы диска (D, D:, D:\ → D).</summary>
public static class DriveLetterHelper
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var s = value.Trim();
        if (s.Length >= 2 && s[1] == ':')
            return char.ToUpperInvariant(s[0]).ToString();

        if (s.Length == 1 && char.IsLetter(s[0]))
            return char.ToUpperInvariant(s[0]).ToString();

        return "";
    }

    public static string ToRootPath(string? letter)
    {
        var l = Normalize(letter);
        return string.IsNullOrEmpty(l) ? "" : $"{l}:\\";
    }

    public static DriveInfo? TryGetDrive(string? letter, bool requireRemovable = false)
    {
        var normalized = Normalize(letter);
        if (string.IsNullOrEmpty(normalized))
            return null;

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!string.Equals(Normalize(drive.Name), normalized, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!drive.IsReady || drive.TotalSize <= 0)
                    continue;
                if (drive.DriveType is DriveType.CDRom or DriveType.Unknown)
                    continue;
                if (requireRemovable && drive.DriveType != DriveType.Removable)
                    continue;

                return drive;
            }
            catch
            {
                // skip
            }
        }

        return null;
    }
}
