using ITService.Backup.Models;

namespace ITService.Backup.Services;

/// <summary>Подключение к локальному SQL и обновление списка баз в конфиге.</summary>
public sealed class SqlConfigSyncService
{
    private readonly SqlBackupService _sql = new();
    private readonly SqlDiscoveryService _discovery = new();

    public Task<SqlConfigSyncResult> SyncAsync(SqlServerSettings sql, CancellationToken ct = default) =>
        SyncAsync(sql, new MachineDiscoveryOptions(), ct);

    public async Task<SqlConfigSyncResult> SyncAsync(
        SqlServerSettings sql,
        MachineDiscoveryOptions options,
        CancellationToken ct = default)
    {
        if (!sql.UseWindowsAuth && string.IsNullOrWhiteSpace(sql.UserName))
        {
            return new SqlConfigSyncResult
            {
                Success = false,
                Message = "Укажите логин SQL в настройках или включите Windows-аутентификацию."
            };
        }

        var configuredTimeout = options.ConfiguredConnectTimeoutSeconds;

        if (!sql.UseLocalServer)
        {
            if (string.IsNullOrWhiteSpace(sql.Instance))
            {
                return new SqlConfigSyncResult
                {
                    Success = false,
                    Message = "Укажите IP или имя SQL Server (как в SSMS)."
                };
            }

            if (await _sql.TryConnectAsync(sql, configuredTimeout, ct))
                return await RefreshDatabaseListAsync(sql, instanceAutoDetected: false, options, ct);

            return new SqlConfigSyncResult
            {
                Success = false,
                Message = $"Не удалось подключиться к SQL «{sql.Instance}». Проверьте адрес, порт и права."
            };
        }

        var instanceChanged = false;
        var hadInstance = !string.IsNullOrWhiteSpace(sql.Instance);

        if (hadInstance && await _sql.TryConnectAsync(sql, configuredTimeout, ct))
            return await RefreshDatabaseListAsync(sql, instanceAutoDetected: false, options, ct);

        var probeTimeout = options.LocalProbeTimeoutSeconds;
        foreach (var candidate in _discovery.GetLocalProbeCandidates(sql.Instance))
        {
            ct.ThrowIfCancellationRequested();
            var probe = CloneWithInstance(sql, candidate);
            if (!await _sql.TryConnectAsync(probe, probeTimeout, ct))
                continue;

            if (!string.Equals(sql.Instance, candidate, StringComparison.OrdinalIgnoreCase))
            {
                sql.Instance = candidate;
                instanceChanged = true;
            }

            var result = await RefreshDatabaseListAsync(sql, instanceAutoDetected: instanceChanged, options, ct);
            if (result.Success)
                return result;
        }

        return new SqlConfigSyncResult
        {
            Success = false,
            Message = hadInstance
                ? $"Не удалось подключиться к SQL «{sql.Instance}». Проверьте имя сервера и права учётной записи."
                : "SQL Server на этом компьютере не найден. Укажите имя сервера в настройках (как в SSMS)."
        };
    }

    public static int MergeDatabaseList(SqlServerSettings sql, IReadOnlyList<string> serverNames, bool enableNewDatabases)
    {
        var existing = sql.Databases.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var newCount = 0;

        sql.Databases = serverNames.Select(name =>
        {
            if (existing.TryGetValue(name, out var old))
                return old;

            newCount++;
            return new DatabaseEntry
            {
                Name = name,
                DisplayName = name,
                Enabled = true
            };
        }).ToList();

        return newCount;
    }

    private async Task<SqlConfigSyncResult> RefreshDatabaseListAsync(
        SqlServerSettings sql,
        bool instanceAutoDetected,
        MachineDiscoveryOptions options,
        CancellationToken ct)
    {
        var names = await _sql.ListUserDatabasesAsync(sql, ct);
        var newCount = MergeDatabaseList(sql, names, options.EnableNewSqlDatabases);

        var parts = new List<string> { $"SQL: {sql.Instance} — баз {names.Count}" };
        if (instanceAutoDetected)
            parts.Add("сервер найден автоматически");
        if (newCount > 0)
            parts.Add($"добавлено {newCount}");
        if (sql.Databases.Count > 0 && newCount == 0)
            parts.Add($"в списке: {sql.Databases.Count}");

        return new SqlConfigSyncResult
        {
            Success = true,
            ConfigChanged = newCount > 0 || instanceAutoDetected,
            InstanceAutoDetected = instanceAutoDetected,
            DatabaseCount = names.Count,
            NewDatabases = newCount,
            Message = string.Join(", ", parts) + "."
        };
    }

    private static SqlServerSettings CloneWithInstance(SqlServerSettings sql, string instance) => new()
    {
        Instance = instance,
        UseWindowsAuth = sql.UseWindowsAuth,
        UserName = sql.UserName,
        Password = sql.Password,
        Databases = sql.Databases
    };
}
