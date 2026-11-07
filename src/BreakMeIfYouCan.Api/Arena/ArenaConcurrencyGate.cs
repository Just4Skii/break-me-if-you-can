using Microsoft.Extensions.Options;

namespace BreakMeIfYouCan.Api.Arena;

/// <summary>
/// Bounded concurrency for database work and for whole runs. The arena is a shared
/// process: without this cap, a burst from one visitor would starve every other visitor
/// and the connection pool. This is deliberately process-wide, not per visitor.
/// </summary>
public sealed class ArenaConcurrencyGate : IDisposable
{
    private readonly SemaphoreSlim _transactions;
    private readonly SemaphoreSlim _runs;

    public ArenaConcurrencyGate(IOptions<ArenaOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _transactions = new SemaphoreSlim(Math.Max(1, options.Value.GlobalConcurrencyLimit));
        _runs = new SemaphoreSlim(Math.Max(1, options.Value.MaxConcurrentRuns));
    }

    public async Task<IDisposable> AcquireTransactionAsync(CancellationToken cancellationToken)
    {
        await _transactions.WaitAsync(cancellationToken);
        return new Releaser(_transactions);
    }

    public async Task<IDisposable> AcquireRunAsync(CancellationToken cancellationToken)
    {
        await _runs.WaitAsync(cancellationToken);
        return new Releaser(_runs);
    }

    public void Dispose()
    {
        _transactions.Dispose();
        _runs.Dispose();
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
