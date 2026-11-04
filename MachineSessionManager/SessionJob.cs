using MachineSessionManager.Email;
using MachineSessionManager.Idira;
using MachineSessionManager.Security;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.Logging;
using SessionManager = MachineSessionManager.Sessions.MachineSessionManager;

namespace MachineSessionManager;

public sealed class SessionJob
{
    private readonly SessionSettings _session;

    private readonly SessionManager _sessionManager;

    private readonly EmailNotification _emailNotification;

    private readonly ILogger<SessionJob> _logger;

    private readonly ImpersonationSettings _impersonationSettings;

    private readonly IIdiraCredentialProvider _credentialProvider;

    private readonly IWindowsImpersonation _impersonation;

    public SessionJob(
        SessionSettings session,
        SessionManager sessionManager,
        EmailNotification emailNotification,
        ImpersonationSettings impersonationSettings,
        IIdiraCredentialProvider credentialProvider,
        IWindowsImpersonation impersonation,
        ILogger<SessionJob> logger)
    {
        _session = session;
        _sessionManager = sessionManager;
        _emailNotification = emailNotification;
        _impersonationSettings = impersonationSettings;
        _credentialProvider = credentialProvider;
        _impersonation = impersonation;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Starting {Mode} for {User} on {Count} machine(s)",
            _session.ModeType,
            _session.UserName,
            _session.Machines.Count);

        var password = await _credentialProvider.GetPasswordAsync(_impersonationSettings.UserName, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var results = await _impersonation.RunAsync(
            _impersonationSettings.UserName,
            password,
            () => _sessionManager.ExecuteAsync(cancellationToken));

        foreach (var result in results)
        {
            _logger.LogInformation(
                "{Machine} | {User} | {SessionId} | {Status} | {Details}",
                result.Machine,
                result.UserName ?? "Unknown",
                result.SessionId?.ToString() ?? "-",
                result.Status,
                result.Details);
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
            _logger.LogError(ex, "Session checks completed, but the email report failed");

            return 2;
        }

        return results.Any(result => result.IsIssue) ? 1 : 0;
    }
}
