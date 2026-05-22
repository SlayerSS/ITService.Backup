using Microsoft.Win32;

namespace ITService.Backup.Services;

/// <summary>Поиск локального SQL Server на машине, где запущена программа.</summary>
public sealed class SqlDiscoveryService
{
    public IReadOnlyList<string> GetLocalServerCandidates(string? configuredInstance)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        void Add(string? name)
        {
            var n = name?.Trim();
            if (string.IsNullOrEmpty(n) || !seen.Add(n))
                return;
            list.Add(n);
        }

        Add(configuredInstance);

        var machine = Environment.MachineName;
        Add(machine);
        Add(".");
        Add("(local)");
        Add("localhost");

        foreach (var instance in ReadInstalledInstanceNames())
        {
            if (string.Equals(instance, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
            {
                Add(machine);
                Add(".");
            }
            else
            {
                Add($"{machine}\\{instance}");
                Add($".\\{instance}");
            }
        }

        return list;
    }

    /// <summary>Короткий список имён для быстрого перебора при старте (без дубликатов localhost).</summary>
    public IReadOnlyList<string> GetLocalProbeCandidates(string? configuredInstance)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        void Add(string? name)
        {
            var n = name?.Trim();
            if (string.IsNullOrEmpty(n) || !seen.Add(n))
                return;
            list.Add(n);
        }

        Add(configuredInstance);

        var machine = Environment.MachineName;
        var instances = ReadInstalledInstanceNames().ToList();
        if (instances.Count == 0)
        {
            Add(machine);
            Add(".");
            return list;
        }

        foreach (var instance in instances)
        {
            if (string.Equals(instance, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
            {
                Add(machine);
                Add(".");
            }
            else
            {
                Add($"{machine}\\{instance}");
                Add($".\\{instance}");
            }
        }

        return list;
    }

    private static IEnumerable<string> ReadInstalledInstanceNames()
    {
        var names = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL",
                false);
            if (key == null)
                return names;

            foreach (var name in key.GetValueNames())
            {
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }
        }
        catch
        {
            // ignore
        }

        return names;
    }
}
