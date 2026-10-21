namespace MachineSessionManager.Models;

// SMTP transport configuration. Message addressing and policy belong to NotificationSettings.
public sealed class EmailSettings
{
    public string SmtpServer { get; }
    public int? Port { get; }
    public bool UseSsl { get; }
    public string? UserName { get; }

    public EmailSettings(
        string smtpServer,
        int? port,
        bool useSsl,
        string? userName)
    {
        SmtpServer = smtpServer;
        Port = port;
        UseSsl = useSsl;
        UserName = userName;
    }
}
