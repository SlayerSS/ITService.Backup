using System.Reflection;
using System.Runtime.InteropServices;

namespace ITService.Backup.Services;

/// <summary>
/// Распаковывает AlphaVSS в %ProgramData%\ITBackup\vss-runtime (сохраняется между запусками, обновляется при смене версии exe).
/// </summary>
internal static class AlphaVssRuntimeBootstrap
{
    private static readonly string[] EmbeddedNames =
    [
        "AlphaVSS.x64.dll",
        "Ijwhost.dll",
        "AlphaVSS.Common.dll"
    ];

    private static readonly string[] LoadOrder =
    [
        "Ijwhost.dll",
        "AlphaVSS.Common.dll",
        "AlphaVSS.x64.dll"
    ];

    private static bool _initialized;
    private static bool _ready;
    private static string? _runtimeDir;

    public static bool IsReady => _ready;

    public static bool EnsureReady()
    {
        if (_initialized)
            return _ready;

        lock (typeof(AlphaVssRuntimeBootstrap))
        {
            if (_initialized)
                return _ready;

            AppPaths.EnsureDataDirectory();
            RemoveLegacyDllsBesideExecutable();

            _runtimeDir = AppPaths.VssRuntimeDirectory;
            Directory.CreateDirectory(_runtimeDir);

            var updated = SyncEmbeddedDlls(_runtimeDir);
            if (!updated || !HasAllDlls(_runtimeDir))
            {
                var missing = GetMissingDlls(_runtimeDir);
                LogService.WarnGlobal(
                    $"VSS: не удалось подготовить DLL ({string.Join(", ", missing)}). " +
                    "Запуск от администратора, переустановите ITBackup из релиза.");
                _ready = false;
            }
            else
            {
                PrependPath(_runtimeDir);
                RegisterAssemblyResolver(_runtimeDir);
                _ready = true;
                var preloadError = TryPreloadAssemblies(_runtimeDir);
                if (preloadError != null)
                    LogService.WarnGlobal($"VSS: предзагрузка: {preloadError}");
            }

            _initialized = true;
            return _ready;
        }
    }

    private static bool SyncEmbeddedDlls(string targetDir)
    {
        var asm = typeof(AlphaVssRuntimeBootstrap).Assembly;
        var ok = true;

        foreach (var fileName in EmbeddedNames)
        {
            var embedded = ReadEmbeddedBytes(asm, fileName);
            if (embedded == null)
            {
                ok = false;
                continue;
            }

            var dest = Path.Combine(targetDir, fileName);
            if (File.Exists(dest) && FilesEqual(dest, embedded))
                continue;

            try
            {
                File.WriteAllBytes(dest, embedded);
            }
            catch
            {
                ok = false;
            }
        }

        return ok && HasAllDlls(targetDir);
    }

    private static byte[]? ReadEmbeddedBytes(Assembly asm, string fileName)
    {
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
            return null;

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static bool FilesEqual(string path, byte[] expected)
    {
        try
        {
            var existing = File.ReadAllBytes(path);
            return existing.AsSpan().SequenceEqual(expected);
        }
        catch
        {
            return false;
        }
    }

    private static void RemoveLegacyDllsBesideExecutable()
    {
        var hostDir = GetHostDirectory();
        if (string.IsNullOrEmpty(hostDir))
            return;

        var programData = AppPaths.VssRuntimeDirectory;
        foreach (var fileName in EmbeddedNames)
        {
            try
            {
                var path = Path.Combine(hostDir, fileName);
                if (!File.Exists(path))
                    continue;
                if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(programData),
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static string GetHostDirectory()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            var dir = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrEmpty(dir))
                return dir;
        }

        return AppContext.BaseDirectory;
    }

    private static bool HasAllDlls(string dir) => GetMissingDlls(dir).Count == 0;

    private static List<string> GetMissingDlls(string dir) =>
        EmbeddedNames.Where(f => !File.Exists(Path.Combine(dir, f))).ToList();

    private static void PrependPath(string dir)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (path.Contains(dir, StringComparison.OrdinalIgnoreCase))
            return;

        Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + path);
    }

    private static void RegisterAssemblyResolver(string dir)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            if (name is not ("AlphaVSS.x64" or "AlphaVSS.Common"))
                return null;

            var path = Path.Combine(dir, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
    }

    private static string? TryPreloadAssemblies(string dir)
    {
        Exception? last = null;

        foreach (var file in LoadOrder)
        {
            var path = Path.Combine(dir, file);
            if (!File.Exists(path))
                continue;

            try
            {
                if (file.StartsWith("AlphaVSS", StringComparison.OrdinalIgnoreCase))
                    Assembly.LoadFrom(path);
                else
                    NativeLibrary.Load(path);
            }
            catch (Exception ex)
            {
                last = ex;
                if (!NativeLibrary.TryLoad(path, out _) && LoadLibrary(path) == IntPtr.Zero)
                    last = ex;
            }
        }

        return last?.Message;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);
}
