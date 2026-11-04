using MachineSessionManager.Reporting;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.Logging;

namespace MachineSessionManager.Email;

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
        bool shouldSend;

        switch (_settings.NotificationModeType)
        {
            case NotificationModeType.Never:
                shouldSend = false;
                break;

            case NotificationModeType.Always:
                shouldSend = true;
                break;

            case NotificationModeType.Relevant:
                // Includes detected/signed-out sessions and every query/logoff error.
                // A fully checked run containing only NotSignedIn results is irrelevant.
                shouldSend = results.Any(result => result.IsRelevant);
                break;

            default:
                throw new InvalidOperationException("Unsupported email notification mode.");
        }

        return shouldSend;
    }

    public async Task NotifyAsync(IReadOnlyList<SessionResult> results, CancellationToken cancellationToken = default)
    {
        if (!ShouldSend(results))
        {
            _logger.LogInformation("Email skipped (notification mode: {Mode})", _settings.NotificationModeType);

            return;
        }

        var report = _reportBuilder.Build(results);
        var message = new EmailMessage(
            from: _settings.From,
            to: _settings.To,
            cc: _settings.Cc,
            bcc: _settings.Bcc,
            report: report);

        await _emailSender.SendAsync(message, cancellationToken);

        _logger.LogInformation("Email report sent to {Count} primary recipient(s)", _settings.To.Count);
    }
}
