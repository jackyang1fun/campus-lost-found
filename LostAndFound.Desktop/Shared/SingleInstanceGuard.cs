namespace LostAndFound.Desktop.Shared;

public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\LostAndFoundSystem.Desktop";
    private Mutex? _mutex;
    private bool _ownsMutex;

    private SingleInstanceGuard(Mutex? mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public static SingleInstanceGuard? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstanceGuard(mutex, ownsMutex: true);
        }

        mutex.Dispose();
        return HasVisibleWindow()
            ? null
            : new SingleInstanceGuard(mutex: null, ownsMutex: false);
    }

    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The process is exiting after a failed startup; ownership may already be gone.
            }
        }

        _mutex.Dispose();
        _mutex = null;
        _ownsMutex = false;
    }

    private static bool HasVisibleWindow()
    {
        foreach (var processName in new[] { "LostAndFound", "LostAndFound.Desktop" })
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                try
                {
                    if (process.Id != Environment.ProcessId &&
                        process.MainWindowHandle != IntPtr.Zero)
                    {
                        return true;
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return false;
    }
}
