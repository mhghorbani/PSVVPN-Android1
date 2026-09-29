using NPViera.Domain;

namespace NPViera.Application;

public sealed class ConnectionStateStore
{
    readonly object sync = new();
    ConnectionState current = ConnectionState.Idle;
    NpvError? lastError;

    public event EventHandler? Changed;

    public ConnectionState Current { get { lock (sync) return current; } }
    public NpvError? LastError { get { lock (sync) return lastError; } }

    public void Set(ConnectionState state, NpvError? error = null)
    {
        lock (sync)
        {
            current = state;
            lastError = error;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
