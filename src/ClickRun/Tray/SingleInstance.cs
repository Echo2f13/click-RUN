namespace ClickRun.Tray;

/// <summary>
/// Ensures only one instance of ClickRun runs at a time using a named Mutex.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = "Global\\ClickRun_SingleInstance_Mutex";
    private readonly Mutex _mutex;
    private readonly bool _isOwner;

    public bool IsFirstInstance => _isOwner;

    public SingleInstance()
    {
        _mutex = new Mutex(true, MutexName, out _isOwner);
    }

    public void Dispose()
    {
        if (_isOwner)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
