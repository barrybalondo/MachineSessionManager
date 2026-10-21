using System.ComponentModel;
using MachineSessionManager.Native;
using static MachineSessionManager.Sessions.SessionResults;

namespace MachineSessionManager.Sessions;

// Owns one safe sign-out operation: eligibility, ownership, request and verification.
public sealed class SessionSignOut
{
    // ERROR_CTX_WINSTATION_NOT_FOUND: the session no longer exists.
    private const int SessionNotFound = 7022;
    // Returned WTS protocol value, distinct from the information-class selector 16.
    private const ushort RdpProtocol = 2;
    private readonly IWtsServerFactory _serverFactory;

    public SessionSignOut(IWtsServerFactory serverFactory)
    {
        _serverFactory = serverFactory;
    }

    public async Task<SessionResult> ExecuteAsync(
        string machine,
        WindowsSession session,
        WindowsSessionUser originalUser,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // This operation owns its connection, including the verification period.
        // `using` releases it on success, early return, failure, or cancellation.
        using var server = _serverFactory.Open(machine);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Session IDs can be reused. Recheck the exact account, including its domain,
            // immediately before acting, even when configuration uses a bare username.
            var currentUser = server.GetUser(session.Id);
            cancellationToken.ThrowIfCancellationRequested();
            if (!SameUser(originalUser, currentUser))
            {
                return MakeResult(machine, session, originalUser, SessionStatusType.AlreadySignedOut,
                    "The original account no longer owns this session; no logoff requested.");
            }

            var protocol = server.GetProtocolType(session.Id);
            cancellationToken.ThrowIfCancellationRequested();
            if (protocol != RdpProtocol)
            {
                return MakeResult(machine, session, originalUser, SessionStatusType.Skipped,
                    "Only RDP sessions are eligible; console or other protocol skipped.");
            }

            server.Logoff(session.Id);
        }
        catch (Win32Exception ex)
        {
            if (ex.NativeErrorCode != SessionNotFound)
            {
                throw;
            }

            return MakeResult(machine, session, originalUser, SessionStatusType.AlreadySignedOut,
                "Session ended before the logoff request completed.");
        }

        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
                var currentUser = server.GetUser(session.Id);
                cancellationToken.ThrowIfCancellationRequested();
                if (!SameUser(originalUser, currentUser))
                {
                    return MakeResult(machine, session, originalUser, SessionStatusType.Success,
                        "Logoff verified; the original account no longer occupies this session.");
                }
            }

            return MakeResult(machine, session, originalUser, SessionStatusType.NotConfirmed,
                "Logoff requested, but the account is still present after three verification checks.");
        }
        catch (Win32Exception ex)
        {
            if (ex.NativeErrorCode == SessionNotFound)
            {
                return MakeResult(machine, session, originalUser, SessionStatusType.Success,
                    "Logoff verified; session no longer exists.");
            }

            return MakeResult(machine, session, originalUser, SessionStatusType.NotConfirmed,
                $"Logoff requested; verification failed: {ex.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MakeResult(machine, session, originalUser, SessionStatusType.NotConfirmed,
                $"Logoff requested; verification failed: {ex.Message}");
        }
    }

    private static bool SameUser(WindowsSessionUser first, WindowsSessionUser second)
    {
        return string.Equals(first.UserName, second.UserName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(first.Domain, second.Domain, StringComparison.OrdinalIgnoreCase);
    }
}
