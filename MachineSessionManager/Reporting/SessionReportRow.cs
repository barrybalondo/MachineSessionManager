namespace MachineSessionManager.Reporting;

public sealed class SessionReportRow
{
    public string Machine { get; }
    public string UserName { get; }
    public string Session { get; }
    public string State { get; }
    public string Status { get; }
    public string Details { get; }
    public string Color { get; }

    public SessionReportRow(
        string machine, string userName, string session, string state, string status, string details, string color)
    {
        Machine = machine;
        UserName = userName;
        Session = session;
        State = state;
        Status = status;
        Details = details;
        Color = color;
    }
}
