namespace NexControl.Computer;

/// <summary>
/// The emergency stop. Every mouse, keyboard and automation operation takes its cancellation token from here,
/// so <see cref="StopEverything"/> interrupts smooth moves, typing, drags, waits and running sequences immediately.
/// </summary>
public sealed class StopController
{
    private CancellationTokenSource _cts = new();
    private readonly object _lock = new();

    /// <summary>Raised (on any thread) after a stop was requested.</summary>
    public event Action<string>? Stopped;

    public CancellationToken Token
    {
        get { lock (_lock) return _cts.Token; }
    }

    /// <summary>A token that is cancelled by an emergency stop or by <paramref name="other"/>.</summary>
    public CancellationTokenSource Link(CancellationToken other) =>
        CancellationTokenSource.CreateLinkedTokenSource(Token, other);

    public void StopEverything(string reason = "Emergency stop")
    {
        CancellationTokenSource old;
        lock (_lock)
        {
            old = _cts;
            _cts = new CancellationTokenSource();
        }
        // Not disposed: other threads may still be holding its token.
        old.Cancel();
        Stopped?.Invoke(reason);
    }
}
