using System.ComponentModel;
using MachineSessionManager.Models;
using MachineSessionManager.Email;
using MachineSessionManager.Sessions;
using MachineSessionManager.Native;
using MachineSessionManager.Reporting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using SessionManager = MachineSessionManager.Sessions.MachineSessionManager;

namespace MachineSessionManager.Tests;

public sealed class SessionJobTests
{
    [Theory]
    [InlineData(NotificationModeType.Never, false, false, 0, 0)]
    [InlineData(NotificationModeType.Never, true, false, 0, 0)]
    [InlineData(NotificationModeType.Never, false, true, 0, 1)]
    [InlineData(NotificationModeType.Always, false, false, 1, 0)]
    [InlineData(NotificationModeType.Always, true, false, 1, 0)]
    [InlineData(NotificationModeType.Always, false, true, 1, 1)]
    [InlineData(NotificationModeType.Relevant, false, false, 0, 0)]
    [InlineData(NotificationModeType.Relevant, true, false, 1, 0)]
    [InlineData(NotificationModeType.Relevant, false, true, 1, 1)]
    public async Task JobAppliesNotificationPolicyAndReturnsSessionOutcome(
        NotificationModeType notificationMode, bool signedIn, bool machineError, int emailCount, int exitCode)
    {
        var session = new SessionSettings(SessionModeType.Check, "DOMAIN\\alice", ["VM"], TimeSpan.FromSeconds(30));
        var server = new FakeWtsServer(signedIn
            ? [new WindowsSession(1, "RDP-Tcp#1", WtsConnectionStateType.Active)]
            : [], _ => new WindowsSessionUser("alice", "DOMAIN"));
        var factory = new FakeWtsServerFactory(_ => machineError ? throw new Win32Exception(1722) : server);
        var sender = new FakeEmailSender();

        var result = await CreateJob(session, notificationMode, factory, sender).RunAsync();

        Assert.Equal(exitCode, result);
        Assert.Equal(emailCount, sender.Reports.Count);
        if (machineError && emailCount > 0)
        {
            Assert.Contains("Unreachable", sender.Reports[0].PlainTextBody);
        }
    }

    [Fact]
    public async Task EmailFailureReturnsFatalExitCodeAfterChecksComplete()
    {
        var queried = false;
        var session = new SessionSettings(SessionModeType.Check, "alice", ["VM"], TimeSpan.FromSeconds(30));
        var factory = new FakeWtsServerFactory(_ =>
        {
            queried = true;
            return new FakeWtsServer([], _ => new WindowsSessionUser("", ""));
        });

        var result = await CreateJob(session, NotificationModeType.Always, factory, new FakeEmailSender(fail: true)).RunAsync();

        Assert.True(queried);
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task UnrequestedEmailCancellationIsAnEmailFailure()
    {
        var session = new SessionSettings(SessionModeType.Check, "alice", ["VM"], TimeSpan.FromSeconds(30));
        var factory = new FakeWtsServerFactory(_ => new FakeWtsServer([], _ => new WindowsSessionUser("", "")));
        var sender = new FakeEmailSender(error: new OperationCanceledException("SMTP operation cancelled."));

        var result = await CreateJob(session, NotificationModeType.Always, factory, sender).RunAsync();

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task ATimeoutStillSendsRelevantEmailAndReturnsAnIssue()
    {
        using var release = new ManualResetEventSlim();
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new SessionSettings(SessionModeType.Check, "alice", ["VM"], TimeSpan.FromSeconds(1));
        var factory = new FakeWtsServerFactory(_ =>
        {
            opened.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return new FakeWtsServer([], _ => new WindowsSessionUser("", ""),
                dispose: () => disposed.TrySetResult());
        });
        var sender = new FakeEmailSender();
        var execution = CreateJob(session, NotificationModeType.Relevant, factory, sender).RunAsync();
        try
        {
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var exitCode = await execution.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, exitCode);
            var report = Assert.Single(sender.Reports);
            Assert.Contains("Timed Out", report.PlainTextBody);
            Assert.Contains("Timed Out", report.HtmlBody);
            Assert.Contains("Attention Required", report.Subject);
            Assert.False(disposed.Task.IsCompleted);
        }
        finally
        {
            release.Set();
            await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ACancelledJobDoesNotOpenMachinesOrSendEmail()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var session = new SessionSettings(SessionModeType.Check, "alice", ["VM"], TimeSpan.FromSeconds(30));
        var opened = false;
        var factory = new FakeWtsServerFactory(_ =>
        {
            opened = true;
            return new FakeWtsServer([], _ => new WindowsSessionUser("", ""));
        });
        var sender = new FakeEmailSender();
        var job = CreateJob(session, NotificationModeType.Always, factory, sender);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.RunAsync(cancellation.Token));

        Assert.False(opened);
        Assert.Empty(sender.Messages);
    }

    private static SessionJob CreateJob(
        SessionSettings session, NotificationModeType notificationMode, IWtsServerFactory factory, FakeEmailSender sender)
    {
        var settings = new NotificationSettings(notificationMode, "sender@example.com", ["alice@example.com"], [], []);
        var notification = new EmailNotification(settings, new ReportBuilder(session), sender,
            NullLogger<EmailNotification>.Instance);
        return new SessionJob(session, new SessionManager(session, factory, new SessionSignOut(factory),
            NullLogger<SessionManager>.Instance), notification,
            NullLogger<SessionJob>.Instance);
    }
}
