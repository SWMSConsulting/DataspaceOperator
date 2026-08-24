#nullable enable
using Microsoft.Data.Sqlite;

namespace DataspaceOperator.Xaf.Blazor.Server;

/// <summary>
/// Folds the SQLite write-ahead log back into the database file on a schedule.
///
/// SQLite's automatic checkpoint (every 1000 WAL pages, ~4 MB) is PASSIVE: it copies pages into
/// the database but can only reset the WAL when no reader holds an older snapshot. Under a server
/// that always has connections in the pool that condition is rarely met, so the WAL keeps being
/// appended to and never shrinks. On 2026-08-24 it had reached 990 MB against an 11 MB database and
/// filled the data volume, after which the app could no longer start.
///
/// A TRUNCATE checkpoint resets the WAL to zero length. It needs the writer lock, so it can return
/// busy while requests are in flight - that is expected and simply retried on the next tick.
/// </summary>
public sealed class SqliteWalCheckpointService(
    IConfiguration configuration,
    ILogger<SqliteWalCheckpointService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var dataSource = SqliteDataSource(configuration.GetConnectionString("ConnectionString"));
        if (dataSource is null)
        {
            logger.LogInformation("WAL checkpoint service idle: connection string is not SQLite.");
            return;
        }

        logger.LogInformation("WAL checkpoint service started for {DataSource}, every {Minutes} min.",
            dataSource, Interval.TotalMinutes);

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckpointAsync(dataSource, stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Never let a failed checkpoint take the host down; the next tick tries again.
                logger.LogWarning(ex, "WAL checkpoint failed.");
            }
        }
    }

    private async Task CheckpointAsync(string dataSource, CancellationToken ct)
    {
        var before = WalSizeBytes(dataSource);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
        }.ToString());
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await using var reader = await command.ExecuteReaderAsync(ct);

        // Result row is (busy, log, checkpointed): busy = 1 means the WAL could not be reset.
        var busy = await reader.ReadAsync(ct) && reader.GetInt32(0) != 0;
        var after = WalSizeBytes(dataSource);

        if (busy)
            logger.LogInformation("WAL checkpoint busy, WAL still {Bytes} bytes; retrying later.", after);
        else
            logger.LogInformation("WAL checkpoint done: {Before} -> {After} bytes.", before, after);
    }

    private static long WalSizeBytes(string dataSource)
    {
        var wal = new FileInfo(dataSource + "-wal");
        return wal.Exists ? wal.Length : 0;
    }

    /// <summary>
    /// Extract the file path from the XAF-style connection string
    /// ("EFCoreProvider=SQLite;Data Source=/path/db"). Returns null for any other provider.
    /// </summary>
    internal static string? SqliteDataSource(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        string? dataSource = null;
        var isSqlite = false;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator < 0) continue;
            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();

            if (key.Equals("EFCoreProvider", StringComparison.OrdinalIgnoreCase))
                isSqlite = value.Equals("SQLite", StringComparison.OrdinalIgnoreCase);
            else if (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("DataSource", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("Filename", StringComparison.OrdinalIgnoreCase))
                dataSource = value;
        }

        return isSqlite && !string.IsNullOrEmpty(dataSource) ? dataSource : null;
    }
}
