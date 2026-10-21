using MachineSessionManager.Reporting;
using MachineSessionManager.Sessions;
using Xunit;

namespace MachineSessionManager.Tests;

public sealed class ReportBuilderTests
{
    [Fact]
    public void TemplateRendersResultsAndEscapesHtmlValues()
    {
        var builder = new ReportBuilder(new SessionSettings(SessionModeType.Check, "DOMAIN\\alice", ["VM"], TimeSpan.FromSeconds(30)));
        var result = new SessionResult("<VM>&", 7, "DOMAIN\\<alice>", "Active",
            SessionStatusType.SignedIn, "<script>alert('test')</script>");

        var report = builder.Build([result]);

        Assert.DoesNotContain("{{", report.HtmlBody);
        Assert.Contains("&lt;VM&gt;&amp;", report.HtmlBody);
        Assert.Contains("DOMAIN\\&lt;alice&gt;", report.HtmlBody);
        Assert.DoesNotContain("<script>", report.HtmlBody);
        Assert.Contains("&lt;script&gt;", report.HtmlBody);
        Assert.Contains("Signed In", report.HtmlBody);
        Assert.Contains("<VM>&", report.PlainTextBody);
        Assert.Contains("State: Active", report.PlainTextBody);
    }

    [Fact]
    public void FailedIdentityQueryIsAnIssueAndNotACountedMatchingSession()
    {
        var builder = new ReportBuilder(new SessionSettings(SessionModeType.Check, "alice", ["VM"], TimeSpan.FromSeconds(30)));
        var result = new SessionResult("VM", 7, null, "Active", SessionStatusType.AccessDenied, "Denied");

        var report = builder.Build([result]);

        Assert.Contains("Attention Required", report.Subject);
        Assert.Contains("Matching sessions:</strong> 0", report.HtmlBody);
        Assert.Contains("Issues:</strong> 1", report.HtmlBody);
    }
}
