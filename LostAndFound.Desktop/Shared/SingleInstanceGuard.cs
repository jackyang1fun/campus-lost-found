namespace LostAndFound.Desktop.Shared;

public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\LostAndFoundSystem.Desktop";
    private Mutex? _mutex;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static SingleInstanceGuard? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstanceGuard(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // The process is exiting after a failed startup; ownership may already be gone.
        }

        _mutex.Dispose();
        _mutex = null;
    }
}
