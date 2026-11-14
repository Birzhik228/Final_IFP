using Microsoft.Data.Sqlite;

namespace LibraryLending.Infrastructure.Persistence;

/// <summary>Creates a consistent SQLite snapshot, including committed WAL data, without replacing files.</summary>
public static class SqliteBackup
{
    public static async Task CreateAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        if (!File.Exists(source)) throw new FileNotFoundException("Source database was not found.", source);
        if (string.Equals(source, destination, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Backup destination must differ from the source.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        // CreateNew prevents overwriting an existing database, including a running production database.
        using (File.Open(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        await using var input = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await using var output = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        await input.OpenAsync(ct); await output.OpenAsync(ct);
        ct.ThrowIfCancellationRequested();
        input.BackupDatabase(output);
        await VerifyAsync(destination, ct);
    }

    public static async Task VerifyAsync(string path, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        if (!string.Equals((await command.ExecuteScalarAsync(ct))?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SQLite integrity verification failed.");
        command.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct)) throw new InvalidDataException("SQLite foreign-key verification failed.");
    }
}
