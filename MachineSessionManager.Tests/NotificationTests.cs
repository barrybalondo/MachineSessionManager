using MachineSessionManager.Email;
using MachineSessionManager.Reporting;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MachineSessionManager.Tests;

public sealed class NotificationTests
{
    public static IEnumerable<object[]> Outcomes()
    {
        foreach (var status in Enum.GetValues<SessionStatusType>())
        {
            yield return [status];
        }
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void NeverSuppressesEveryOutcome(SessionStatusType status)
    {
        Assert.False(Create(NotificationModeType.Never).ShouldSend([Result(status)]));
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void AlwaysIncludesEveryOutcome(SessionStatusType status)
    {
        Assert.True(Create(NotificationModeType.Always).ShouldSend([Result(status)]));
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void RelevantIncludesActivityAndErrorsButNotAnEmptyMachine(SessionStatusType status)
    {
        Assert.Equal(
            status != SessionStatusType.NotSignedIn,
            Create(NotificationModeType.Relevant).ShouldSend([Result(status)]));
    }

    [Fact]
    public void AnErrorMakesAnOtherwiseEmptyRunRelevant()
    {
        Assert.True(Create(NotificationModeType.Relevant).ShouldSend([Result(SessionStatusType.NotSignedIn), Result(SessionStatusType.Unreachable)]));
    }

    [Fact]
    public async Task NotificationSuppliesAddressesAndReportToTheSender()
    {
        var settings = new NotificationSettings(
            notificationMode: NotificationModeType.Always,
            from: "sender@example.com",
            to: ["alice@example.com"],
            cc: ["copy@example.com"],
            bcc: ["blind@example.com"]);
        var session = new SessionSettings(
            mode: SessionModeType.Check,
            userName: "alice",
            machines: ["VM"],
            machineTimeout: TimeSpan.FromSeconds(30));
        var sender = new FakeEmailSender();
        var notification = new EmailNotification(
            settings: settings,
            reportBuilder: new ReportBuilder(session),
            emailSender: sender,
            logger: NullLogger<EmailNotification>.Instance);

        await notification.NotifyAsync([Result(SessionStatusType.SignedIn)]);

        var message = Assert.Single(sender.Messages);
        Assert.Equal("sender@example.com", message.From);
        Assert.Equal("alice@example.com", Assert.Single(message.To));
        Assert.Equal("copy@example.com", Assert.Single(message.Cc));
        Assert.Equal("blind@example.com", Assert.Single(message.Bcc));
        Assert.Contains("Machine Session Check", message.Report.Subject);
        Assert.Contains("User: alice", message.Report.PlainTextBody);
    }

    private static EmailNotification Create(NotificationModeType notificationMode)
    {
        var settings = new NotificationSettings(
            notificationMode: notificationMode,
            from: "sender@example.com",
            to: ["alice@example.com"],
            cc: [],
            bcc: []);
        var session = new SessionSettings(
            mode: SessionModeType.Check,
            userName: "alice",
            machines: ["VM"],
            machineTimeout: TimeSpan.FromSeconds(30));
        return new EmailNotification(
            settings: settings,
            reportBuilder: new ReportBuilder(session),
            emailSender: new FakeEmailSender(),
            logger: NullLogger<EmailNotification>.Instance);
    }

    private static SessionResult Result(SessionStatusType status)
    {
        return new SessionResult(
            machine: "VM",
            sessionId: null,
            userName: null,
            state: "-",
            status: status,
            details: "Test result");
    }
}
