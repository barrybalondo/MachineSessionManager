using MachineSessionManager.Email;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.Logging;
using SessionManager = MachineSessionManager.Sessions.MachineSessionManager;

namespace MachineSessionManager;

// Owns the application workflow. Dependencies are provided once by Program's DI setup.
public sealed class SessionJob
{
    private readonly SessionSettings _session;

    private readonly SessionManager _sessionManager;

    private readonly EmailNotification _emailNotification;

    private readonly ILogger<SessionJob> _logger;

    public SessionJob(
        SessionSettings session,
        SessionManager sessionManager,
        EmailNotification emailNotification,
        ILogger<SessionJob> logger)
    {
        _session = session;
        _sessionManager = sessionManager;
        _emailNotification = emailNotification;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting {Mode} for {User} on {Count} machine(s)",
            _session.ModeType, _session.UserName, _session.Machines.Count);

        var results = await _sessionManager.ExecuteAsync(cancellationToken);
        foreach (var result in results)
        {
            _logger.LogInformation("{Machine} | {User} | {SessionId} | {Status} | {Details}",
                result.Machine, result.UserName ?? "Unknown", result.SessionId?.ToString() ?? "-",
                result.Status, result.Details);
        }

        try
        {
            await _emailNotification.NotifyAsync(results, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Exit codes belong to the job, not the email notification or SMTP sender.
            _logger.LogError(ex, "Session checks completed, but the email report failed");
            return 2;
        }

        return results.Any(result => result.IsIssue) ? 1 : 0;
    }
}
