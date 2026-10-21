namespace MachineSessionManager.Reporting;

// The explicit contract between ReportBuilder and the Scriban HTML template.
// Display strings are HTML-encoded by the builder before entering this model.
public sealed class SessionReportModel
{
    public string UserName { get; }

    public string Action { get; }

    public string RunTime { get; }

    public int MachineCount { get; }

    public int SessionCount { get; }

    public int ActivityCount { get; }

    public int IssueCount { get; }

    public IReadOnlyList<SessionReportRow> Results { get; }

    public SessionReportModel(
        string userName,
        string action,
        string runTime,
        int machineCount,
        int sessionCount,
        int activityCount,
        int issueCount,
        IEnumerable<SessionReportRow> results)
    {
        UserName = userName;
        Action = action;
        RunTime = runTime;
        MachineCount = machineCount;
        SessionCount = sessionCount;
        ActivityCount = activityCount;
        IssueCount = issueCount;
        Results = Array.AsReadOnly(results.ToArray());
    }
}
