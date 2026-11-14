using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using LibraryLending.Application;
using LibraryLending.Domain;
using LibraryLending.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var settings = BenchmarkSettings.Parse(args);
Directory.CreateDirectory(settings.OutputDirectory);
var databasePath = Path.Combine(settings.OutputDirectory, $"benchmark-{Guid.NewGuid():N}.db");
var connection = new SqliteConnectionStringBuilder { DataSource = databasePath, DefaultTimeout = 5 }.ToString();
var services = new ServiceCollection();
services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Error));
services.AddSingleton(TimeProvider.System);
services.AddSingleton(new LibraryOptions());
services.AddSingleton<INotificationPublisher, QuietPublisher>();
services.AddSingleton<LibraryWriteCoordinator>();
services.AddDbContextFactory<LibraryDbContext>(options => options.UseSqlite(connection));
services.AddScoped<ILibraryRepository, EfLibraryRepository>();
services.AddScoped<ILibraryService, LibraryService>();
await using var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<IDbContextFactory<LibraryDbContext>>();
var seedTimer = Stopwatch.StartNew();
await SeedAsync(factory, settings);
seedTimer.Stop();
var actor = new Actor("benchmark-librarian", true, null);

// Warm the same application/repository code that the measured scenarios will exercise.
await using (var scope = provider.CreateAsyncScope())
{
    var library = scope.ServiceProvider.GetRequiredService<ILibraryService>();
    await library.SearchBooksAsync(new BookQuery("", "", false, 1, 20));
    await library.GetDashboardAsync(actor);
    await library.GetReservationsAsync(new Actor("benchmark-member", false, 901));
}
for (var iteration = 0; iteration < 20; iteration++)
{
    await ReadAsync(0, iteration);
    await WriteAsync(0, iteration);
}

var results = new List<ScenarioResult>();
foreach (var concurrency in settings.Concurrency)
{
    results.Add(await RunAsync("read-mix", concurrency, settings.Seconds, ReadAsync));
    results.Add(await RunAsync("borrow-return", concurrency, settings.Seconds, WriteAsync));
    Console.WriteLine($"Completed concurrency {concurrency}: {results[^2].Completed} read operations, {results[^1].Completed} borrow/return pairs.");
}
var consistency = await CheckConsistencyAsync();
var journalMode = await JournalModeAsync();
var report = new
{
    generatedAtUtc = DateTimeOffset.UtcNow,
    environment = new { os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription, logicalProcessors = Environment.ProcessorCount },
    dataset = new { books = settings.Books, copies = settings.Books * 2, members = settings.Members, initialOpenLoans = 800, historicalLoans = 5000, waitingReservations = 400, seedSeconds = seedTimer.Elapsed.TotalSeconds, journalMode },
    method = "Single-process service-level benchmark using the real application, EF Core repository, migrations and an isolated file-backed SQLite database. No HTTP, Blazor circuit, network or SignalR transport costs are included.",
    databasePath,
    scenarios = results,
    consistency
};
var output = Path.Combine(settings.OutputDirectory, "results.json");
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Report: {output}");
Console.WriteLine($"Consistency: {(consistency.Passed ? "PASS" : "FAIL")}; operation errors: {results.Sum(x => x.Errors)}");
return consistency.Passed && results.All(x => x.Errors == 0) ? 0 : 1;

async Task ReadAsync(int worker, long iteration)
{
    await using var scope = provider.CreateAsyncScope();
    var library = scope.ServiceProvider.GetRequiredService<ILibraryService>();
    switch (iteration % 10)
    {
        case 7:
            var dashboard = await library.GetDashboardAsync(actor);
            if (dashboard.Books != settings.Books) throw new InvalidOperationException("Dashboard lost catalogue records.");
            break;
        case 8:
            var memberId = 801 + worker % 100;
            var reservations = await library.GetReservationsAsync(new Actor("benchmark-member", false, memberId));
            if (reservations.Any(x => x.MemberId != memberId)) throw new InvalidOperationException("Reservation query leaked another member's data.");
            break;
        case 9:
            var overdue = await library.GetLoansAsync(actor, true, "Member 00", "due");
            if (overdue.Any(x => x.DaysOverdue <= 0)) throw new InvalidOperationException("Overdue query returned an on-time loan.");
            break;
        default:
            var page = await library.SearchBooksAsync(new BookQuery("Book", "", iteration % 2 == 0, 1 + (int)(iteration % 80), 20));
            if (page.Items.Count > 20) throw new InvalidOperationException("Catalogue page exceeded its requested size.");
            break;
    }
}

async Task WriteAsync(int worker, long iteration)
{
    await using var scope = provider.CreateAsyncScope();
    var library = scope.ServiceProvider.GetRequiredService<ILibraryService>();
    // Each worker owns a different copy/member. Concurrent SQLite writers still contend.
    var borrow = await library.BorrowAsync(actor, settings.Books * 2 - worker, settings.Members - worker);
    if (!borrow.IsSuccess) throw new InvalidOperationException("Borrow: " + borrow.Error);
    var returned = await library.ReturnAsync(actor, borrow.Value);
    if (!returned.IsSuccess) throw new InvalidOperationException("Return: " + returned.Error);
}

async Task<ScenarioResult> RunAsync(string name, int concurrency, int seconds, Func<int, long, Task> action)
{
    // Keep each scenario's starting history equal; cleanup is outside the measurement.
    await using (var cleanup = await factory.CreateDbContextAsync())
        await cleanup.Loans.Where(x => x.MemberId > 800 && x.ReturnedAtUtc != null).ExecuteDeleteAsync();
    var latencies = new ConcurrentBag<double>();
    var failures = new ConcurrentDictionary<string, int>();
    using var process = Process.GetCurrentProcess();
    process.Refresh();
    var cpuBefore = process.TotalProcessorTime;
    var workingBefore = process.WorkingSet64;
    long peakWorking = workingBefore, peakPrivate = process.PrivateMemorySize64;
    using var sampleCancellation = new CancellationTokenSource();
    var sampler = Task.Run(async () =>
    {
        while (!sampleCancellation.IsCancellationRequested)
        {
            process.Refresh();
            peakWorking = Math.Max(peakWorking, process.WorkingSet64);
            peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
            try { await Task.Delay(200, sampleCancellation.Token); } catch (OperationCanceledException) { break; }
        }
    });
    var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var timer = new Stopwatch();
    var workers = Enumerable.Range(0, concurrency).Select(worker => Task.Run(async () =>
    {
        await start.Task;
        long iteration = worker;
        while (timer.Elapsed.TotalSeconds < seconds)
        {
            var operation = Stopwatch.StartNew();
            try { await action(worker, iteration++); latencies.Add(operation.Elapsed.TotalMilliseconds); }
            catch (Exception error) { failures.AddOrUpdate(error.Message, 1, (_, count) => count + 1); }
        }
    })).ToArray();
    timer.Start(); start.SetResult();
    await Task.WhenAll(workers);
    timer.Stop(); sampleCancellation.Cancel(); await sampler;
    process.Refresh();
    var cpuSeconds = (process.TotalProcessorTime - cpuBefore).TotalSeconds;
    var samples = latencies.Order().ToArray();
    static double Percentile(double[] sorted, double percentile) => sorted.Length == 0 ? 0 : sorted[(int)Math.Ceiling(percentile * sorted.Length) - 1];
    return new ScenarioResult(name, concurrency, seconds, timer.Elapsed.TotalSeconds, samples.Length, failures.Values.Sum(), samples.Length / timer.Elapsed.TotalSeconds,
        Percentile(samples, .50), Percentile(samples, .95), Percentile(samples, .99), samples.LastOrDefault(), cpuSeconds,
        cpuSeconds / timer.Elapsed.TotalSeconds * 100, cpuSeconds / timer.Elapsed.TotalSeconds / Environment.ProcessorCount * 100,
        workingBefore, process.WorkingSet64, peakWorking, peakPrivate, failures);
}

async Task<ConsistencyResult> CheckConsistencyAsync()
{
    async Task<Result<int>> CompetingBorrow(int copyId, int memberId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILibraryService>().BorrowAsync(actor, copyId, memberId);
    }
    var sameCopy = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => CompetingBorrow(settings.Books * 2 - 100, 901 + i))));
    await ReturnWinnersAsync(sameCopy);
    await using (var db = await factory.CreateDbContextAsync())
    {
        var member = await db.Members.SingleAsync(x => x.Id == 899);
        member.BorrowingLimit = 1;
        await db.SaveChangesAsync();
    }
    var sameMember = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => CompetingBorrow(settings.Books * 2 - 110 - i, 899))));
    await ReturnWinnersAsync(sameMember);
    await using var check = await factory.CreateDbContextAsync();
    var duplicateLoans = await check.Loans.Where(x => x.ReturnedAtUtc == null).GroupBy(x => x.BookCopyId).CountAsync(g => g.Count() > 1);
    var invalidBorrowed = await check.Copies.CountAsync(c => c.Status == CopyStatus.Borrowed && check.Loans.Count(l => l.BookCopyId == c.Id && l.ReturnedAtUtc == null) != 1);
    var invalidAvailable = await check.Copies.CountAsync(c => c.Status == CopyStatus.Available && check.Loans.Any(l => l.BookCopyId == c.Id && l.ReturnedAtUtc == null));
    var leakedBenchmarkLoans = await check.Loans.CountAsync(l => l.MemberId > 800 && l.ReturnedAtUtc == null);
    var counts = new ConsistencyResult(sameCopy.Count(x => x.IsSuccess), sameMember.Count(x => x.IsSuccess), duplicateLoans, invalidBorrowed, invalidAvailable, leakedBenchmarkLoans);
    return counts;
}

async Task ReturnWinnersAsync(IEnumerable<Result<int>> outcomes)
{
    foreach (var outcome in outcomes.Where(x => x.IsSuccess))
    {
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ILibraryService>().ReturnAsync(actor, outcome.Value);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error);
    }
}

async Task<string> JournalModeAsync()
{
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.OpenConnectionAsync();
    await using var command = db.Database.GetDbConnection().CreateCommand();
    command.CommandText = "PRAGMA journal_mode";
    return Convert.ToString(await command.ExecuteScalarAsync()) ?? "unknown";
}

static async Task SeedAsync(IDbContextFactory<LibraryDbContext> factory, BenchmarkSettings settings)
{
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
    var categories = Enumerable.Range(1, 10).Select(i => new Category { Name = $"Category {i:00}" }).ToArray();
    var authors = Enumerable.Range(1, 100).Select(i => new Author { Name = $"Author {i:000}" }).ToArray();
    var books = Enumerable.Range(1, settings.Books).Select(i =>
    {
        var book = new Book { Title = $"Benchmark Book {i:00000}", Isbn = $"978{i:0000000000}", Year = 2020 + i % 5, Category = categories[i % categories.Length], Description = "Deterministic, synthetic load-test record." };
        book.BookAuthors.Add(new BookAuthor { Book = book, Author = authors[i % authors.Length], DisplayOrder = 0 });
        book.Copies.Add(new BookCopy { Book = book, Barcode = $"BENCH-{i:00000}-1", Status = CopyStatus.Available });
        book.Copies.Add(new BookCopy { Book = book, Barcode = $"BENCH-{i:00000}-2", Status = CopyStatus.Available });
        return book;
    }).ToArray();
    db.Books.AddRange(books);
    db.Members.AddRange(Enumerable.Range(1, settings.Members).Select(i => new Member { Name = $"Member {i:0000}", Email = $"member{i}@benchmark.test", Status = MembershipStatus.Active, BorrowingLimit = 5 }));
    await db.SaveChangesAsync();
    var copies = books.SelectMany(b => b.Copies).OrderBy(c => c.Id).ToArray();
    var now = DateTime.UtcNow;
    var today = DateOnly.FromDateTime(now);
    for (var i = 0; i < 800; i++)
    {
        copies[i].Status = CopyStatus.Borrowed;
        db.Loans.Add(new Loan { BookCopyId = copies[i].Id, MemberId = i + 1, BorrowedAtUtc = now.AddDays(-20), DueDate = today.AddDays(i % 2 == 0 ? -6 : 8), DailyFineMinor = 100 });
    }
    for (var i = 0; i < 5000; i++)
        db.Loans.Add(new Loan { BookCopyId = copies[i % copies.Length].Id, MemberId = i % 800 + 1, BorrowedAtUtc = now.AddDays(-40), DueDate = today.AddDays(-26), ReturnedAtUtc = now.AddDays(-30), DailyFineMinor = 100, FinalFineMinor = 0 });
    for (var i = 0; i < 400; i++)
        db.Reservations.Add(new Reservation { BookId = books[i].Id, MemberId = 801 + i % 100, Status = ReservationStatus.Waiting, CreatedAtUtc = now.AddDays(-1).AddSeconds(i) });
    await db.SaveChangesAsync();
}

internal sealed class QuietPublisher : INotificationPublisher
{
    public Task PublishAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
}
internal sealed record ScenarioResult(string Workload, int Concurrency, int RequestedSeconds, double ElapsedSeconds, int Completed, int Errors, double OperationsPerSecond,
    double P50Milliseconds, double P95Milliseconds, double P99Milliseconds, double MaximumMilliseconds, double CpuSeconds, double CpuPercentOfOneCore, double CpuPercentOfMachine,
    long WorkingSetBeforeBytes, long WorkingSetAfterBytes, long PeakWorkingSetBytes, long PeakPrivateBytes, IReadOnlyDictionary<string, int> ErrorMessages);
internal sealed record ConsistencyResult(int SameCopyWinners, int SameMemberWinners, int DuplicateOpenLoans, int InvalidBorrowedCopies, int InvalidAvailableCopies, int LeakedBenchmarkLoans)
{
    public bool Passed => SameCopyWinners == 1 && SameMemberWinners == 1 && DuplicateOpenLoans == 0 && InvalidBorrowedCopies == 0 && InvalidAvailableCopies == 0 && LeakedBenchmarkLoans == 0;
}
internal sealed record BenchmarkSettings(int Books, int Members, int Seconds, int[] Concurrency, string OutputDirectory)
{
    public static BenchmarkSettings Parse(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("Each option requires a value.");
            options[args[i]] = args[i + 1];
        }
        var result = new BenchmarkSettings(int.Parse(options.GetValueOrDefault("--books", "3000")), int.Parse(options.GetValueOrDefault("--members", "1000")),
            int.Parse(options.GetValueOrDefault("--seconds", "20")), options.GetValueOrDefault("--concurrency", "1,10,25").Split(',').Select(int.Parse).ToArray(),
            Path.GetFullPath(options.GetValueOrDefault("--output", "artifacts/performance")));
        if (result.Books < 1000 || result.Members < 1000 || result.Seconds < 1 || result.Concurrency.Any(x => x < 1 || x > 100)) throw new ArgumentException("Use at least 1000 books/members, positive duration and concurrency 1–100.");
        return result;
    }
}
