using System.ComponentModel;
using MachineSessionManager.Email;
using MachineSessionManager.Native;
using MachineSessionManager.Reporting;
using MachineSessionManager.Security;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using SessionManager = MachineSessionManager.Sessions.MachineSessionManager;

namespace MachineSessionManager.Tests;

public sealed class SessionJobTests
{
    private const string ImpersonationUser = "svc_sessions@company.com";

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
        NotificationModeType notificationMode,
        bool signedIn,
        bool machineError,
        int emailCount,
        int exitCode)
    {
        var session = new SessionSettings(
            mode: SessionModeType.Check,
            userName: "DOMAIN\\alice",
            machines: ["VM"],
            machineTimeout: TimeSpan.FromSeconds(30));
        var server = new FakeWtsServer(
            signedIn
                ? [new WindowsSession(1, "RDP-Tcp#1", WtsConnectionStateType.Active)]
                : [],
            _ => new WindowsSessionUser("alice", "DOMAIN"));
        var factory = new FakeWtsServerFactory(_ => machineError ? throw new Win32Exception(1722) : server);
        var sender = new FakeEmailSender();

        var result = await CreateJob(
            session: session,
            notificationMode: notificationMode,
            factory: factory,
            sender: sender).RunAsync();

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
        var session = new SessionSettings(
            mode: SessionModeType.Check,
            userName: "alice",
            machines: ["VM"],
            machineTimeout: TimeSpan.FromSeconds(30));
        var factory = new FakeWtsServerFactory(
            _ =>
            {
                queried = true;
                return new FakeWtsServer([], _ => new WindowsSessionUser("", ""));
            });

        var result = await CreateJob(
            session: session,
            notificationMode: NotificationModeType.Always,
            factory: factory,
            sender: new FakeEmailSender(fail: true)).RunAsync();

        Assert.True(queried);
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task UnrequestedEmailCancellationIsAnEmailFailure()
    {
        var session = new SessionSettings(
            mode: SessionModeType.Check,
            userName: "alice",
            machines: ["VM"],
            machineTimeout: TimeSpan.FromSeconds(30));
        var factory = new FakeWtsServerFactory(_ => new FakeWtsServer([], _ => new WindowsSessionUser("", "")));
        var sender = new FakeEmailSender(error: new OperationCanceledException("SMTP operation cancelled."));

        var result = await CreateJob(
            session: session,
            notificationMode: NotificationModeType.Always,
            factory: factory,
            sender: sender).RunAsync();

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task ATimeoutStillSendsRelevantEmailAndReturnsAnIssue()
    {
        var impersonation = new FakeWindowsImpersonation();
        using var release = new ManualResetEventSlim();
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new SessionSettings(
            mode: SessionModeType.Check,
            userName: "alice",
            machines: ["VM"],
            machineTimeout: TimeSpan.FromSeconds(1));
        var factory = new FakeWtsServerFactory(
            _ =>
            {
                Assert.Equal(ImpersonationUser, impersonation.CurrentUserName);
                opened.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                return new FakeWtsServer(
                    [],
                    _ => new WindowsSessionUser("", ""),
                    dispose: () =>
                    {
                        Assert.Equal(ImpersonationUser, impersonation.CurrentUserName);
                        disposed.TrySetResult();
                    });
            });
        var sender = new FakeEmailSender(onSend: () => Assert.Null(impersonation.CurrentUserName));
        var execution = CreateJob(
            session: session,
            notificationMode: NotificationModeType.Relevant,
            factory: factory,
            sender: sender,
            impersonation: impersonation).RunAsync();
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
        var session = new SessionSettings(
            mode: SessionModeType.Check,
            userName: "alice",
            machines: ["VM"],
            machineTimeout: TimeSpan.FromSeconds(30));
        var opened = false;
        var factory = new FakeWtsServerFactory(
            _ =>
            {
                opened = true;
                return new FakeWtsServer([], _ => new WindowsSessionUser("", ""));
            });
        var sender = new FakeEmailSender();
        var credentials = new FakeIdiraCredentialProvider();
        var impersonation = new FakeWindowsImpersonation();
        var job = CreateJob(
            session: session,
            notificationMode: NotificationModeType.Always,
            factory: factory,
            sender: sender,
            credentials: credentials,
            impersonation: impersonation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.RunAsync(cancellation.Token));

        Assert.False(opened);
        Assert.Empty(sender.Messages);
        Assert.Equal(0, credentials.CallCount);
        Assert.Equal(0, impersonation.CallCount);
    }

    [Fact]
    public async Task RetrievesOncePerRunAndImpersonatesAllMachinesWithoutChangingTheTargetUser()
    {
        var credentials = new FakeIdiraCredentialProvider();
        var impersonation = new FakeWindowsImpersonation();
        var session = new SessionSettings(
            mode: SessionModeType.Check,
            userName: "DOMAIN\\alice",
            machines: ["ONE", "TWO"],
            machineTimeout: TimeSpan.FromSeconds(30));
        var opens = 0;
        var factory = new FakeWtsServerFactory(
            _ =>
            {
                Assert.Equal(ImpersonationUser, impersonation.CurrentUserName);
                Interlocked.Increment(ref opens);
                return new FakeWtsServer(
                    [new WindowsSession(1, "RDP-Tcp#1", WtsConnectionStateType.Active)],
                    _ =>
                    {
                        Assert.Equal(ImpersonationUser, impersonation.CurrentUserName);
                        return new WindowsSessionUser("alice", "DOMAIN");
                    });
            });
        var sender = new FakeEmailSender(onSend: () => Assert.Null(impersonation.CurrentUserName));
        var job = CreateJob(
            session: session,
            notificationMode: NotificationModeType.Always,
            factory: factory,
            sender: sender,
            credentials: credentials,
            impersonation: impersonation);

        Assert.Equal(0, await job.RunAsync());
        Assert.Equal(1, credentials.CallCount);
        Assert.Equal(ImpersonationUser, credentials.RequestedUserName);
        Assert.Equal(1, impersonation.CallCount);
        Assert.Equal(FakeIdiraCredentialProvider.Password, impersonation.ReceivedPassword);
        Assert.Equal(2, opens);
        Assert.Contains("DOMAIN\\alice", Assert.Single(sender.Reports).PlainTextBody);
        Assert.Null(impersonation.CurrentUserName);

        Assert.Equal(0, await job.RunAsync());
        Assert.Equal(2, credentials.CallCount);
        Assert.Equal(2, impersonation.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CredentialOrLogonFailureStopsBeforeAnyRemoteOperation(bool retrievalFailure)
    {
        var failure = new InvalidOperationException("Simulated credential failure.");
        var credentials = new FakeIdiraCredentialProvider(retrievalFailure ? failure : null);
        var impersonation = new FakeWindowsImpersonation(retrievalFailure ? null : failure);
        var opened = false;
        var factory = new FakeWtsServerFactory(
            _ =>
            {
                opened = true;
                return new FakeWtsServer([], _ => new WindowsSessionUser("", ""));
            });
        var session = new SessionSettings(
            mode: SessionModeType.Check,
            userName: "alice",
            machines: ["VM"],
            machineTimeout: TimeSpan.FromSeconds(30));
        var sender = new FakeEmailSender();
        var job = CreateJob(
            session: session,
            notificationMode: NotificationModeType.Always,
            factory: factory,
            sender: sender,
            credentials: credentials,
            impersonation: impersonation);

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => job.RunAsync()));
        Assert.False(opened);
        Assert.Empty(sender.Messages);
        Assert.Equal(retrievalFailure ? 0 : 1, impersonation.CallCount);
    }

    [Fact]
    public async Task CancellationAfterRetrievalPreventsLogonAndRemoteWork()
    {
        using var cancellation = new CancellationTokenSource();
        var credentials = new FakeIdiraCredentialProvider(onGet: cancellation.Cancel);
        var impersonation = new FakeWindowsImpersonation();
        var factory = new FakeWtsServerFactory(_ => throw new InvalidOperationException("Must not open a machine."));
        var session = new SessionSettings(
            mode: SessionModeType.Check,
            userName: "alice",
            machines: ["VM"],
            machineTimeout: TimeSpan.FromSeconds(30));
        var sender = new FakeEmailSender();
        var job = CreateJob(
            session: session,
            notificationMode: NotificationModeType.Always,
            factory: factory,
            sender: sender,
            credentials: credentials,
            impersonation: impersonation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.RunAsync(cancellation.Token));

        Assert.Equal(0, impersonation.CallCount);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task SignOutAndItsAsynchronousVerificationUseTheImpersonationContext()
    {
        var impersonation = new FakeWindowsImpersonation();
        var session = new SessionSettings(
            mode: SessionModeType.SignOut,
            userName: "DOMAIN\\alice",
            machines: ["VM"],
            machineTimeout: TimeSpan.FromSeconds(30));
        var signedOut = false;
        var userQueries = 0;
        var server = new FakeWtsServer(
            sessions: [new WindowsSession(1, "RDP-Tcp#1", WtsConnectionStateType.Active)],
            readUser: _ =>
            {
                Assert.Equal(ImpersonationUser, impersonation.CurrentUserName);
                userQueries++;
                return signedOut ? new WindowsSessionUser("", "") : new WindowsSessionUser("alice", "DOMAIN");
            },
            protocol: _ =>
            {
                Assert.Equal(ImpersonationUser, impersonation.CurrentUserName);
                return 2;
            },
            logoff: _ =>
            {
                Assert.Equal(ImpersonationUser, impersonation.CurrentUserName);
                signedOut = true;
            });
        var factory = new FakeWtsServerFactory(
            _ =>
            {
                Assert.Equal(ImpersonationUser, impersonation.CurrentUserName);
                return server;
            });
        var sender = new FakeEmailSender(onSend: () => Assert.Null(impersonation.CurrentUserName));

        var result = await CreateJob(
            session: session,
            notificationMode: NotificationModeType.Always,
            factory: factory,
            sender: sender,
            impersonation: impersonation).RunAsync();

        Assert.Equal(0, result);
        Assert.Single(server.LogoffRequests);
        Assert.Equal(3, userQueries);
        Assert.Contains("Success", Assert.Single(sender.Reports).PlainTextBody);
    }

    private static SessionJob CreateJob(
        SessionSettings session,
        NotificationModeType notificationMode,
        IWtsServerFactory factory,
        FakeEmailSender sender,
        FakeIdiraCredentialProvider? credentials = null,
        FakeWindowsImpersonation? impersonation = null)
    {
        var settings = new NotificationSettings(
            notificationMode: notificationMode,
            from: "sender@example.com",
            to: ["alice@example.com"],
            cc: [],
            bcc: []);
        var notification = new EmailNotification(
            settings: settings,
            reportBuilder: new ReportBuilder(session),
            emailSender: sender,
            logger: NullLogger<EmailNotification>.Instance);
        return new SessionJob(
            session: session,
            sessionManager: new SessionManager(
                settings: session,
                serverFactory: factory,
                signOut: new SessionSignOut(factory),
                logger: NullLogger<SessionManager>.Instance),
            emailNotification: notification,
            impersonationSettings: new ImpersonationSettings(ImpersonationUser),
            credentialProvider: credentials ?? new FakeIdiraCredentialProvider(),
            impersonation: impersonation ?? new FakeWindowsImpersonation(),
            logger: NullLogger<SessionJob>.Instance);
    }
}
