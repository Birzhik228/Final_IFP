using LibraryLending.Infrastructure.Persistence;

try
{
    if (args.Length == 2 && args[0] == "verify") await SqliteBackup.VerifyAsync(args[1]);
    else if (args.Length == 3 && args[0] is "backup" or "restore") await SqliteBackup.CreateAsync(args[1], args[2]);
    else
    {
        Console.Error.WriteLine("Usage: verify <database> | backup <source> <new-snapshot> | restore <snapshot> <new-database>");
        return 2;
    }
    Console.WriteLine("Database operation completed and integrity verified.");
    return 0;
}
catch (Exception exception) when (exception is IOException or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
{
    Console.Error.WriteLine($"Database operation failed: {exception.Message}");
    return 1;
}
