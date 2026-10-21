using MachineSessionManager.Models;
using MailKit.Security;
using MimeKit;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace MachineSessionManager.Email;

public sealed class EmailSender : IEmailSender
{
    private readonly EmailSettings _settings;

    public EmailSender(EmailSettings settings)
    {
        _settings = settings;
    }

    public async Task SendAsync(EmailMessage email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email.From) || email.To.Count == 0 ||
            string.IsNullOrWhiteSpace(_settings.SmtpServer))
        {
            throw new InvalidOperationException(
                "Notification:From, Notification:To, and Email:SmtpServer are required when emailing.");
        }

        var hasLogin = !string.IsNullOrWhiteSpace(_settings.UserName);
        if (hasLogin && !_settings.UseSsl)
        {
            throw new InvalidOperationException(
                "Email:UseSsl must be true when SMTP credentials are configured.");
        }

        string? password = null;
        if (hasLogin)
        {
            password = Environment.GetEnvironmentVariable("MACHINESESSION_SMTP_PASSWORD");
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException(
                    "MACHINESESSION_SMTP_PASSWORD is not set for the scheduled account.");
            }
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(email.From));
        foreach (var address in email.To)
        {
            message.To.Add(MailboxAddress.Parse(address));
        }

        foreach (var address in email.Cc)
        {
            message.Cc.Add(MailboxAddress.Parse(address));
        }

        foreach (var address in email.Bcc)
        {
            message.Bcc.Add(MailboxAddress.Parse(address));
        }

        message.Subject = email.Report.Subject;
        message.Body = new BodyBuilder
        {
            TextBody = email.Report.PlainTextBody,
            HtmlBody = email.Report.HtmlBody
        }.ToMessageBody();

        using var client = new SmtpClient();
        var security = _settings.UseSsl
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        await client.ConnectAsync(_settings.SmtpServer, _settings.Port ?? 0, security, cancellationToken);
        if (hasLogin)
        {
            await client.AuthenticateAsync(_settings.UserName!, password!, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
