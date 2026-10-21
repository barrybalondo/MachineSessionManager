namespace MachineSessionManager;

internal sealed class ConsoleCancellation : IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _source = new();
    private bool _disposed;
    private int _cancellationCalls;

    public CancellationToken Token { get; }

    public ConsoleCancellation()
    {
        Token = _source.Token;
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    public void Cancel()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _cancellationCalls++;
        }

        try
        {
            _source.Cancel();
        }
        finally
        {
            lock (_sync)
            {
                _cancellationCalls--;
                if (_disposed && _cancellationCalls == 0)
                {
                    _source.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            // A running Cancel owns the source until its callbacks finish.
            // Deferring disposal also handles cleanup from inside a callback.
            if (_cancellationCalls == 0)
            {
                _source.Dispose();
            }
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        Cancel();
    }
}
