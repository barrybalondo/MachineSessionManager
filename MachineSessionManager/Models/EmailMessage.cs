namespace MachineSessionManager.Models;

// A fully addressed report ready for delivery. No notification policy travels to SMTP.
public sealed class EmailMessage
{
    public string From { get; }
    public IReadOnlyList<string> To { get; }
    public IReadOnlyList<string> Cc { get; }
    public IReadOnlyList<string> Bcc { get; }
    public EmailReport Report { get; }

    public EmailMessage(
        string from,
        IEnumerable<string> to,
        IEnumerable<string> cc,
        IEnumerable<string> bcc,
        EmailReport report)
    {
        From = from;
        To = Array.AsReadOnly(to.ToArray());
        Cc = Array.AsReadOnly(cc.ToArray());
        Bcc = Array.AsReadOnly(bcc.ToArray());
        Report = report;
    }
}
