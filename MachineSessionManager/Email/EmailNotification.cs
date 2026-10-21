using MachineSessionManager.Reporting;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.Logging;

namespace MachineSessionManager.Email;

// Owns the notification decision and coordinates report creation/delivery.
// SMTP connections and MIME messages belong to EmailSender.
public sealed class EmailNotification
{
    private readonly NotificationSettings _settings;
    private readonly ReportBuilder _reportBuilder;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<EmailNotification> _logger;

    public EmailNotification(
        NotificationSettings settings,
        ReportBuilder reportBuilder,
        IEmailSender emailSender,
        ILogger<EmailNotification> logger)
    {
        _settings = settings;
        _reportBuilder = reportBuilder;
        _emailSender = emailSender;
        _logger = logger;
    }

    public bool ShouldSend(IReadOnlyList<SessionResult> results)
    {
        return _settings.NotificationModeType switch
        {
            NotificationModeType.Never => false,
            NotificationModeType.Always => true,
            // Includes detected/signed-out sessions and every query/logoff error.
            // A fully checked run containing only NotSignedIn results is irrelevant.
            NotificationModeType.Relevant => results.Any(result => result.IsRelevant),
            _ => throw new InvalidOperationException("Unsupported email notification mode.")
        };
    }

    public async Task NotifyAsync(IReadOnlyList<SessionResult> results, CancellationToken cancellationToken = default)
    {
        if (!ShouldSend(results))
        {
            _logger.LogInformation("Email skipped (notification mode: {Mode})", _settings.NotificationModeType);
            return;
        }

        var report = _reportBuilder.Build(results);
        var message = new EmailMessage(_settings.From, _settings.To, _settings.Cc, _settings.Bcc, report);
        await _emailSender.SendAsync(message, cancellationToken);
        _logger.LogInformation("Email report sent to {Count} primary recipient(s)", _settings.To.Count);
    }
}
