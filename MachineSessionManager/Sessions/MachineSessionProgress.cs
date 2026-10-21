using MachineSessionManager.Models;

namespace MachineSessionManager.Sessions;

// One machine's worker and timeout waiter share this state. Report models remain immutable.
// Completing the snapshot freezes its contents, so late work cannot change the final report.
internal sealed class MachineSessionProgress
{
    public bool HasResults
    {
        get
        {
            lock (_gate)
            {
                return _results.Count > 0;
            }
        }
    }

    private readonly object _gate = new();
    private readonly List<SessionResult> _results = [];
    private SessionResult? _pending;
    private bool _completed;

    public void SetPending(SessionResult? result)
    {
        lock (_gate)
        {
            if (!_completed)
            {
                _pending = result;
            }
        }
    }

    public void Add(SessionResult result)
    {
        lock (_gate)
        {
            if (!_completed)
            {
                _results.Add(result);
                _pending = null;
            }
        }
    }

    public IReadOnlyList<SessionResult> Complete(SessionResult? timeoutResult = null)
    {
        lock (_gate)
        {
            if (!_completed)
            {
                if (timeoutResult is not null)
                {
                    _results.Add(_pending ?? timeoutResult);
                }

                _completed = true;
            }

            return Array.AsReadOnly(_results.ToArray());
        }
    }
}
