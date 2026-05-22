using Alphaleonis.Win32.Vss;

namespace ITService.Backup.Services;

/// <summary>Теневое копирование (VSS) для чтения занятых файлов. Обычно нужны права администратора.</summary>
public sealed class ShadowCopyService : IDisposable
{
    private readonly Dictionary<string, string> _volumeToDevice = new(StringComparer.OrdinalIgnoreCase);
    private IVssBackupComponents? _backup;
    private bool _disposed;

    public bool TryCreateForPath(string sourcePath)
    {
        var volume = GetVolumeName(sourcePath);
        if (_volumeToDevice.ContainsKey(volume))
            return true;

        try
        {
            _backup ??= CreateBackupComponents();
            var setId = _backup.StartSnapshotSet();
            var snapshotId = _backup.AddToSnapshotSet(volume, setId);
            _backup.PrepareForBackup();
            _backup.DoSnapshotSet();

            var props = _backup.GetSnapshotProperties(snapshotId);
            var device = props.SnapshotDeviceObject;
            if (!device.EndsWith('\\'))
                device += '\\';

            _volumeToDevice[volume] = device;
            LogService.Info($"VSS: снимок тома {volume} -> {device}");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error($"VSS недоступен для {volume}: {ex.Message}");
            return false;
        }
    }

    public string ResolveReadPath(string originalPath)
    {
        var full = Path.GetFullPath(originalPath);
        var volume = GetVolumeName(full);
        if (!_volumeToDevice.TryGetValue(volume, out var deviceRoot))
            return full;

        var relative = full.Substring(volume.Length).TrimStart('\\');
        return string.IsNullOrEmpty(relative)
            ? deviceRoot.TrimEnd('\\')
            : Path.Combine(deviceRoot, relative);
    }

    private static IVssBackupComponents CreateBackupComponents()
    {
        AlphaVssRuntimeBootstrap.EnsureReady();
        var factory = VssFactoryProvider.Default.GetVssFactory();
        var backup = factory.CreateVssBackupComponents();
        backup.InitializeForBackup(null);
        backup.SetContext(VssSnapshotContext.Backup);
        return backup;
    }

    private static string GetVolumeName(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "C:\\";
        return root.TrimEnd('\\');
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _backup?.Dispose();
        _backup = null;
        _volumeToDevice.Clear();
    }
}
