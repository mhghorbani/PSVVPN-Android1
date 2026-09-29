namespace NPViera.Domain;

public enum ConnectionState
{
    Idle,
    Preparing,
    Connecting,
    Authenticating,
    Establishing,
    Connected,
    Reconnecting,
    Disconnecting,
    Blocked,
    Error
}
