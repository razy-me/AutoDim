namespace AutoDim.App.Shell;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    public bool IsPrimaryInstance { get; }

    public SingleInstanceGuard(string name = @"Local\AutoDim_SingleInstance_Mutex")
    {
        _mutex = new Mutex(initiallyOwned: true, name: name, out bool createdNew);
        IsPrimaryInstance = createdNew;
    }

    public void Dispose()
    {
        if (IsPrimaryInstance)
        {
            try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
        }
        _mutex.Dispose();
    }
}
