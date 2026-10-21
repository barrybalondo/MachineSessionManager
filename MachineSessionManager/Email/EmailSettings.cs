namespace MachineSessionManager.Email;

// SMTP transport configuration. Message addressing and policy belong to NotificationSettings.
public sealed class EmailSettings
{
    public string Host { get; }
    public int? Port { get; }
    public bool UseSsl { get; }

    public EmailSettings(
        string host,
        int? port,
        bool useSsl)
    {
        Host = host;
        Port = port;
        UseSsl = useSsl;
    }
}
