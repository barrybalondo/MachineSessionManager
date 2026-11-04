using System.Net;
using MachineSessionManager.Email;
using MachineSessionManager.Sessions;
using Scriban;

namespace MachineSessionManager.Reporting;

public sealed class ReportBuilder
{
    private const string TemplateFileName = "SessionReport.html";

    private readonly SessionSettings _settings;

    public ReportBuilder(SessionSettings settings)
    {
        _settings = settings;
    }

    public EmailReport Build(IReadOnlyList<SessionResult> results)
    {
        var runTime = DateTimeOffset.Now;
        var issueCount = results.Count(r => r.IsIssue);
        var activityCount = results.Count(r => r.IsRelevant && !r.IsIssue);

        var action = _settings.ModeType == SessionModeType.Check ? "Check" : "Sign-out";
        var outcome = issueCount > 0
            ? "Attention Required"
            : activityCount > 0 ? "Completed" : "No Sessions";

        var subject = $"Machine Session {action} - {_settings.UserName} - {outcome}";

        var templateFile = Path.Combine(AppContext.BaseDirectory, "Templates", TemplateFileName);

        var template = Template.Parse(File.ReadAllText(templateFile));
        if (template.HasErrors)
        {
            throw new InvalidOperationException($"Invalid {TemplateFileName}: " + string.Join("; ", template.Messages));
        }

        var model = new SessionReportModel(
            userName: Html(_settings.UserName),
            action: Html(action),
            runTime: runTime.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            machineCount: _settings.Machines.Count,
            // Failed identity queries have an ID but are not confirmed matching sessions.
            sessionCount: results.Count(r => r.UserName is not null),
            activityCount: activityCount,
            issueCount: issueCount,
            results: results.Select(
                r => new SessionReportRow(
                    machine: Html(r.Machine),
                    userName: Html(r.UserName ?? "Unknown"),
                    session: r.SessionId?.ToString() ?? "-",
                    state: Html(r.State),
                    status: Html(FormatStatus(r.Status)),
                    details: Html(r.Details),
                    color: r.Status switch
                    {
                        SessionStatusType.Success or SessionStatusType.AlreadySignedOut => "#15803d",
                        SessionStatusType.SignedIn => "#1d4ed8",
                        SessionStatusType.NotSignedIn => "#64748b",
                        _ => "#b45309"
                    })));

        var htmlBody = template.Render(model, member => member.Name);
        var plainTextBody = BuildPlainText(results, runTime);
        return new EmailReport(subject, htmlBody, plainTextBody);
    }

    private string BuildPlainText(IReadOnlyList<SessionResult> results, DateTimeOffset runTime)
    {
        var lines = new List<string>
        {
            $"Machine Session {_settings.ModeType}",
            $"User: {_settings.UserName}",
            $"Run Time: {runTime:yyyy-MM-dd HH:mm:ss zzz}",
            ""
        };

        foreach (var r in results)
        {
            lines.Add($"{r.Machine} | User: {r.UserName ?? "Unknown"} | Session: {r.SessionId?.ToString() ?? "-"} | State: {r.State} | {FormatStatus(r.Status)} | {r.Details}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatStatus(SessionStatusType status) => status switch
    {
        SessionStatusType.NotSignedIn => "Not Signed In",
        SessionStatusType.SignedIn => "Signed In",
        SessionStatusType.AlreadySignedOut => "Already Signed Out",
        SessionStatusType.AccessDenied => "Access Denied",
        SessionStatusType.NotConfirmed => "Not Confirmed",
        SessionStatusType.TimedOut => "Timed Out",
        _ => status.ToString()
    };

    private static string Html(string value)
    {
        // Scriban does not automatically HTML-escape model values.
        return WebUtility.HtmlEncode(value);
    }
}
