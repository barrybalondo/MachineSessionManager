namespace MachineSessionManager.Models;

// Notification policy and message addressing, independent of the SMTP connection.
public sealed class NotificationSettings
{
    public NotificationModeType NotificationModeType { get; }
    public string From { get; }
    public IReadOnlyList<string> To { get; }
    public IReadOnlyList<string> Cc { get; }
    public IReadOnlyList<string> Bcc { get; }

    public NotificationSettings(
        NotificationModeType notificationMode,
        string from,
        IEnumerable<string> to,
        IEnumerable<string> cc,
        IEnumerable<string> bcc)
    {
        if (!Enum.IsDefined(notificationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(notificationMode));
        }

        NotificationModeType = notificationMode;
        From = from;
        To = Array.AsReadOnly(to.ToArray());
        Cc = Array.AsReadOnly(cc.ToArray());
        Bcc = Array.AsReadOnly(bcc.ToArray());
    }
}
