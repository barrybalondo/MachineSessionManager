using MachineSessionManager.Native;
using Microsoft.Extensions.Logging;
using static MachineSessionManager.Sessions.SessionResults;

namespace MachineSessionManager.Sessions;

public sealed class MachineSessionManager
{
    private readonly SessionSettings _settings;

    private readonly IWtsServerFactory _serverFactory;

    private readonly SessionSignOut _signOut;

    private readonly ILogger<MachineSessionManager> _logger;

    public MachineSessionManager(
        SessionSettings settings,
        IWtsServerFactory serverFactory,
        SessionSignOut signOut,
        ILogger<MachineSessionManager> logger)
    {
        _settings = settings;
        _serverFactory = serverFactory;
        _signOut = signOut;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SessionResult>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tasks = _settings.Machines
            .Select(machine => ExecuteMachineWithTimeoutAsync(machine, cancellationToken))
            .ToArray();

        // WhenAll returns results in task/input order, regardless of completion order.
        // Each machine also retains the session order returned by Windows.
        var machineResults = await Task.WhenAll(tasks);
        return Array.AsReadOnly(machineResults.SelectMany(results => results).ToArray());
    }

    private async Task<IReadOnlyList<SessionResult>> ExecuteMachineWithTimeoutAsync(
        string machine, CancellationToken cancellationToken)
    {
        using var machineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var machineToken = machineCancellation.Token;
        var progress = new MachineSessionProgress();

        // The WTS calls are synchronous. The worker owns its handles and may outlive
        // our wait; never dispose its connection from the timeout/cancellation path.
        var work = Task.Run(() => ExecuteMachineAsync(machine, progress, machineToken), machineToken);
        try
        {
            await work.WaitAsync(_settings.MachineTimeout, cancellationToken);
            return progress.Complete();
        }
        catch (TimeoutException)
        {
            machineCancellation.Cancel();
            var results = progress.Complete(MakeResult(machine, null, null, SessionStatusType.TimedOut,
                $"Machine check exceeded {_settings.MachineTimeout:c}. Any remaining sessions were not checked."));
            _ = ObserveCompletionAsync(work, machine);
            return results;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            machineCancellation.Cancel();
            progress.Complete();
            _ = ObserveCompletionAsync(work, machine);
            throw;
        }
    }

    private async Task ExecuteMachineAsync(
        string machine, MachineSessionProgress progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var server = _serverFactory.Open(machine);
            cancellationToken.ThrowIfCancellationRequested();
            var sessions = server.GetSessions();

            foreach (var session in sessions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WindowsSessionUser? user = null;
                progress.SetPending(MakeResult(machine, session, null, SessionStatusType.TimedOut,
                    $"Session query exceeded the machine timeout of {_settings.MachineTimeout:c}. Any remaining sessions were not checked."));
                try
                {
                    user = server.GetUser(session.Id);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(user.UserName) || !IsMatch(user))
                    {
                        progress.SetPending(null);
                        continue;
                    }

                    SessionResult result;
                    if (_settings.ModeType == SessionModeType.SignOut)
                    {
                        progress.SetPending(MakeResult(machine, session, user, SessionStatusType.NotConfirmed,
                            "Machine check timed out during sign-out; the request may already have reached Windows. Any remaining sessions were not checked."));
                        result = await _signOut.ExecuteAsync(machine, session, user, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    else if (_settings.ModeType == SessionModeType.Check)
                    {
                        result = MakeResult(machine, session, user, SessionStatusType.SignedIn, "Session detected.");
                    }
                    else
                    {
                        throw new InvalidOperationException("Unsupported session mode.");
                    }

                    progress.Add(result);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Preserve other results; an unreadable account is not NotSignedIn.
                    progress.Add(MakeResult(machine, session, user, Classify(ex), ex.Message));
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!progress.HasResults)
            {
                progress.Add(MakeResult(machine, null, null, SessionStatusType.NotSignedIn,
                    "No matching user session found."));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Add(MakeResult(machine, null, null, Classify(ex), ex.Message));
        }
    }

    private async Task ObserveCompletionAsync(Task work, string machine)
    {
        try
        {
            await work;
        }
        catch (OperationCanceledException)
        {
            // Expected when a timed-out native call returns and sees cancellation.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background work failed on {Machine} after we stopped waiting", machine);
        }
    }

    private bool IsMatch(WindowsSessionUser user)
    {
        var actual = _settings.UserName.Contains('\\') ? user.AccountName : user.UserName;
        return string.Equals(actual, _settings.UserName, StringComparison.OrdinalIgnoreCase);
    }
}
