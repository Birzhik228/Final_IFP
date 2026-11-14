namespace LibraryLending.Infrastructure.Persistence;

// SQLite admits one writer at a time. Queue local writers asynchronously instead of
// blocking request threads in SQLite's lock wait; the database still guards other processes.
public sealed class LibraryWriteCoordinator
{
    private readonly SemaphoreSlim gate = new(1, 1);

    internal async Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? ownedGate = gate;
        public void Dispose() => Interlocked.Exchange(ref ownedGate, null)?.Release();
    }
}
