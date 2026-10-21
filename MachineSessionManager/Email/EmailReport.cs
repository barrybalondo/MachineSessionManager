namespace MachineSessionManager.Email;

public sealed class EmailReport
{
    public string Subject { get; }

    public string HtmlBody { get; }

    public string PlainTextBody { get; }

    public EmailReport(string subject, string htmlBody, string plainTextBody)
    {
        Subject = subject;
        HtmlBody = htmlBody;
        PlainTextBody = plainTextBody;
    }
}
