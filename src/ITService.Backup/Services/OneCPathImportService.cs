using System.Text;
using System.Text.RegularExpressions;
using ITService.Backup.Models;
using Microsoft.Win32;

namespace ITService.Backup.Services;

/// <summary>Список баз из 1С текущего пользователя (ibases.v8i, 1CEStart, реестр).</summary>
public sealed class OneCPathImportService
{
    private static readonly string[] CatalogFolderNames = ["1Cv8", "1CEStart", "1Cv82"];
    private static readonly string[] KnownCatalogFileNames = ["ibases.v8i", "1CEStart.v8i", "1cestart.v8i"];
    private static readonly Regex FileConnectRegex = new(
        @"File\s*=\s*""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ServerConnectRegex = new(
        @"Srvr\s*=\s*""([^""]+)""\s*;\s*Ref\s*=\s*""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<OneCDiscoveredPath> DiscoverForCurrentUser()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<OneCDiscoveredPath>();

        foreach (var file in FindIbasesFiles())
            ParseIbasesFile(file, list, seen);

        DiscoverFromRegistry(list, seen);

        return SortDiscovered(list);
    }

    /// <summary>Разбор одного файла списка баз 1С (.v8i).</summary>
    public IReadOnlyList<OneCDiscoveredPath> DiscoverFromV8iFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<OneCDiscoveredPath>();
        ParseIbasesFile(Path.GetFullPath(filePath), list, seen);
        return SortDiscovered(list);
    }

    /// <summary>Каталог по умолчанию для диалога выбора .v8i.</summary>
    public static string? GetDefaultV8iBrowseFolder()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var sub in CatalogFolderNames)
        {
            var dir = Path.Combine(appData, "1C", sub);
            if (Directory.Exists(dir))
                return dir;
        }

        var oneC = Path.Combine(appData, "1C");
        return Directory.Exists(oneC) ? oneC : null;
    }

    /// <summary>Добавляет записи в список, пропуская дубликаты по <see cref="OneCDiscoveredPath.Path"/>.</summary>
    public static int MergeInto(List<OneCDiscoveredPath> target, IEnumerable<OneCDiscoveredPath> incoming)
    {
        var seen = new HashSet<string>(
            target.Select(p => p.Path),
            StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var item in incoming)
        {
            if (!seen.Add(item.Path))
                continue;

            target.Add(item);
            added++;
        }

        return added;
    }

    private static List<OneCDiscoveredPath> SortDiscovered(List<OneCDiscoveredPath> list) =>
        list
            .OrderBy(p => p.Kind)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<string> FindIbasesFiles()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        var oneCRoots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1C"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "1C")
        };

        foreach (var oneCRoot in oneCRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(oneCRoot))
                continue;

            foreach (var catalogName in CatalogFolderNames)
            {
                var catalogDir = Path.Combine(oneCRoot, catalogName);
                if (!Directory.Exists(catalogDir))
                    continue;

                foreach (var knownName in KnownCatalogFileNames)
                {
                    var knownPath = Path.Combine(catalogDir, knownName);
                    if (File.Exists(knownPath) && found.Add(knownPath))
                        paths.Add(knownPath);
                }

                AddV8iFilesFromDirectory(catalogDir, found, paths);

                foreach (var sub in SafeEnumerateDirectories(catalogDir))
                {
                    foreach (var knownName in KnownCatalogFileNames)
                    {
                        var nestedKnown = Path.Combine(sub, knownName);
                        if (File.Exists(nestedKnown) && found.Add(nestedKnown))
                            paths.Add(nestedKnown);
                    }

                    AddV8iFilesFromDirectory(sub, found, paths);
                }
            }
        }

        return paths;
    }

    private static void AddV8iFilesFromDirectory(string directory, HashSet<string> found, List<string> paths)
    {
        foreach (var v8i in SafeEnumerateFiles(directory, "*.v8i"))
        {
            if (found.Add(v8i))
                paths.Add(v8i);
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void ParseIbasesFile(string filePath, List<OneCDiscoveredPath> list, HashSet<string> seen)
    {
        string? currentName = null;
        string? currentConnect = null;

        foreach (var rawLine in ReadV8iLines(filePath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                FlushEntry(currentName, currentConnect, filePath, list, seen);
                currentName = line[1..^1].Trim();
                currentConnect = null;
                continue;
            }

            if (line.StartsWith("Connect=", StringComparison.OrdinalIgnoreCase))
                currentConnect = line["Connect=".Length..].Trim();
        }

        FlushEntry(currentName, currentConnect, filePath, list, seen);
    }

    private static void FlushEntry(
        string? name,
        string? connect,
        string sourceFile,
        List<OneCDiscoveredPath> list,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(connect))
            return;

        var entry = ParseConnectLine(name.Trim(), connect.Trim(), Path.GetFileName(sourceFile));
        if (entry == null)
            return;

        if (!seen.Add(entry.Path))
            return;

        list.Add(entry);
    }

    private static OneCDiscoveredPath? ParseConnectLine(string name, string connect, string sourceLabel)
    {
        var fileMatch = FileConnectRegex.Match(connect);
        if (fileMatch.Success)
        {
            var path = NormalizeFilePath(fileMatch.Groups[1].Value);
            if (string.IsNullOrEmpty(path))
                return null;

            return new OneCDiscoveredPath
            {
                Name = name,
                Path = path,
                Kind = OneCPathKind.File,
                Source = sourceLabel
            };
        }

        var srvMatch = ServerConnectRegex.Match(connect);
        if (srvMatch.Success)
        {
            var server = srvMatch.Groups[1].Value.Trim();
            var reference = srvMatch.Groups[2].Value.Trim();
            return new OneCDiscoveredPath
            {
                Name = name,
                Path = $"{server} / {reference}",
                Kind = OneCPathKind.Server,
                Source = sourceLabel
            };
        }

        return null;
    }

    private static IEnumerable<string> ReadV8iLines(string filePath)
    {
        try
        {
            return File.ReadAllLines(filePath, Encoding.UTF8);
        }
        catch
        {
            try
            {
                return File.ReadAllLines(filePath, Encoding.GetEncoding(1251));
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }

    private static string NormalizeFilePath(string path)
    {
        path = path.Replace('/', '\\').Trim();
        if (path.EndsWith(".1cd", StringComparison.OrdinalIgnoreCase))
            path = Path.GetDirectoryName(path) ?? path;

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static void DiscoverFromRegistry(List<OneCDiscoveredPath> list, HashSet<string> seen)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(@"Software\1C\1Cv8");
            if (root == null)
                return;

            foreach (var versionKeyName in root.GetSubKeyNames())
            {
                using var versionKey = root.OpenSubKey(versionKeyName);
                if (versionKey == null)
                    continue;

                foreach (var valueName in versionKey.GetValueNames())
                {
                    if (!valueName.Contains("Path", StringComparison.OrdinalIgnoreCase)
                        && !valueName.Contains("Connect", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (versionKey.GetValue(valueName) is not string raw || string.IsNullOrWhiteSpace(raw))
                        continue;

                    if (!raw.Contains("File=", StringComparison.OrdinalIgnoreCase)
                        && !raw.Contains("Srvr=", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var entry = ParseConnectLine(valueName, raw, "реестр 1С");
                    if (entry == null || !seen.Add(entry.Path))
                        continue;

                    list.Add(entry);
                }
            }
        }
        catch
        {
            // ignore registry read errors
        }
    }

    /// <summary>Добавляет файловые базы 1С в конфиг (новые — с Enabled=true).</summary>
    public static int MergeFileFolders(
        List<FolderEntry> folders,
        IEnumerable<OneCDiscoveredPath> discovered,
        ICollection<string>? newFolderIds = null)
    {
        var existing = folders.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var item in discovered.Where(p => p.Kind == OneCPathKind.File))
        {
            if (existing.ContainsKey(item.Path))
                continue;

            if (!Directory.Exists(item.Path))
                continue;

            var entry = ToFolderEntry(item);
            folders.Add(entry);
            existing[item.Path] = entry;
            newFolderIds?.Add(entry.Id);
            added++;
        }

        return added;
    }

    public static FolderEntry ToFolderEntry(OneCDiscoveredPath discovered, bool enabled = true)
    {
        var folder = new FolderEntry
        {
            Path = discovered.Path,
            DisplayName = discovered.Name,
            Enabled = enabled,
            Archive = true,
            CompressionLevel = "optimal"
        };

        if (discovered.Kind == OneCPathKind.File)
            OneCDiscovery.ApplyToFolder(folder, defaultCopyOnlyBase: true);

        return folder;
    }
}
