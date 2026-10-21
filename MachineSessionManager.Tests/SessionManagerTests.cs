using System.ComponentModel;
using MachineSessionManager.Models;
using MachineSessionManager.Native;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using SessionManager = MachineSessionManager.Sessions.MachineSessionManager;

namespace MachineSessionManager.Tests;

public sealed class SessionManagerTests
{
    [Fact]
    public async Task FailedSessionQueriesPreserveOtherResultsOnTheSameMachine()
    {
        var server = new FakeWtsServer([Session(1), Session(2), Session(3), Session(4)], id => id switch
        {
            2 => throw new Win32Exception(5),
            4 => throw new Win32Exception(1722),
            _ => Alice()
        });

        var results = await Create(SessionModeType.Check, server).ExecuteAsync();

        Assert.Equal(new[] { SessionStatusType.SignedIn, SessionStatusType.AccessDenied,
            SessionStatusType.SignedIn, SessionStatusType.Unreachable }, results.Select(result => result.Status));
        Assert.Equal(new int?[] { 1, 2, 3, 4 }, results.Select(result => result.SessionId));
        Assert.Empty(server.LogoffRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MachineOpenOrEnumerationFailureDoesNotStopOtherMachines(bool failEnumeration)
    {
        var working = new FakeWtsServer([Session(1)], _ => Alice());
        var broken = new FakeWtsServer([], _ => Alice(), enumerationError: new Win32Exception(5));
        var factory = new FakeWtsServerFactory(machine =>
        {
            if (machine == "BROKEN")
            {
                if (failEnumeration)
                {
                    return broken;
                }

                throw new Win32Exception(5);
            }

            return working;
        });
        var manager = new SessionManager(new SessionSettings(SessionModeType.Check, "DOMAIN\\alice",
            ["FIRST", "BROKEN", "LAST"], TimeSpan.FromSeconds(30)), factory, new SessionSignOut(factory),
            NullLogger<SessionManager>.Instance);

        var results = await manager.ExecuteAsync();

        Assert.Equal(new[] { "FIRST", "BROKEN", "LAST" }, results.Select(result => result.Machine));
        Assert.Equal(new[] { SessionStatusType.SignedIn, SessionStatusType.AccessDenied, SessionStatusType.SignedIn },
            results.Select(result => result.Status));
    }

    [Fact]
    public async Task UnrequestedCancellationInAQueryIsAnErrorAndOtherSessionsContinue()
    {
        var server = new FakeWtsServer([Session(1), Session(2)], id => id == 1
            ? throw new OperationCanceledException("The query cancelled without a job cancellation.")
            : Alice());

        var results = await Create(SessionModeType.Check, server).ExecuteAsync();

        Assert.Equal(new[] { SessionStatusType.Error, SessionStatusType.SignedIn },
            results.Select(result => result.Status));
        Assert.Equal(new int?[] { 1, 2 }, results.Select(result => result.SessionId));
    }

    [Fact]
    public async Task UnrequestedCancellationOpeningAMachineDoesNotStopOtherMachines()
    {
        var factory = new FakeWtsServerFactory(machine => machine == "BROKEN"
            ? throw new OperationCanceledException("The connection cancelled without a job cancellation.")
            : new FakeWtsServer([Session(1)], _ => Alice()));
        var settings = new SessionSettings(SessionModeType.Check, "DOMAIN\\alice",
            ["BROKEN", "WORKING"], TimeSpan.FromSeconds(30));
        var manager = new SessionManager(settings, factory, new SessionSignOut(factory),
            NullLogger<SessionManager>.Instance);

        var results = await manager.ExecuteAsync();

        Assert.Equal(new[] { "BROKEN", "WORKING" }, results.Select(result => result.Machine));
        Assert.Equal(new[] { SessionStatusType.Error, SessionStatusType.SignedIn },
            results.Select(result => result.Status));
    }

    [Fact]
    public async Task UnreadableSessionsAreNotReportedAsNotSignedIn()
    {
        var server = new FakeWtsServer([Session(1), Session(2)], _ => throw new Win32Exception(5));
        var results = await Create(SessionModeType.Check, server).ExecuteAsync();

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Equal(SessionStatusType.AccessDenied, result.Status));
    }

    [Fact]
    public async Task OtherAccountsAndEmptyServiceSessionsDoNotMatch()
    {
        var server = new FakeWtsServer([Session(1), Session(2)], id => id == 1
            ? new WindowsSessionUser("alice", "OTHER")
            : new WindowsSessionUser("", ""));

        var result = Assert.Single(await Create(SessionModeType.Check, server).ExecuteAsync());

        Assert.Equal(SessionStatusType.NotSignedIn, result.Status);
        Assert.Null(result.SessionId);
    }

    [Theory]
    [InlineData("domain\\ALICE")]
    [InlineData("ALICE")]
    public async Task UserMatchingSupportsQualifiedAndBareNames(string userName)
    {
        var server = new FakeWtsServer([Session(1)], _ => Alice());
        var manager = Create(SessionModeType.Check, server, userName);

        var result = Assert.Single(await manager.ExecuteAsync());

        Assert.Equal(SessionStatusType.SignedIn, result.Status);
        Assert.Equal("DOMAIN\\alice", result.UserName);
    }

    [Theory]
    [InlineData(WtsConnectionStateType.Active)]
    [InlineData(WtsConnectionStateType.Disconnected)]
    public async Task SignOutTargetsBothActiveAndDisconnectedRdpSessions(WtsConnectionStateType state)
    {
        var server = new FakeWtsServer([new WindowsSession(1, "RDP-Tcp#1", state)], _ => Alice(),
            logoff: _ => throw new Win32Exception(5));

        var result = Assert.Single(await Create(SessionModeType.SignOut, server).ExecuteAsync());

        Assert.Equal(new[] { 1 }, server.LogoffRequests);
        Assert.Equal(SessionStatusType.AccessDenied, result.Status);
    }

    [Fact]
    public async Task FailedLogoffDoesNotStopNextSession()
    {
        var server = new FakeWtsServer([Session(1), Session(2)], _ => Alice(),
            logoff: _ => throw new Win32Exception(5));

        var results = await Create(SessionModeType.SignOut, server).ExecuteAsync();

        Assert.Equal(new[] { 1, 2 }, server.LogoffRequests);
        Assert.All(results, result => Assert.Equal(SessionStatusType.AccessDenied, result.Status));
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    public async Task NonRdpProtocolsAreSkippedEvenWithRdpStationNames(ushort protocol)
    {
        var server = new FakeWtsServer([Session(1)], _ => Alice(), protocol: _ => protocol);

        var result = Assert.Single(await Create(SessionModeType.SignOut, server).ExecuteAsync());

        Assert.Equal(SessionStatusType.Skipped, result.Status);
        Assert.Empty(server.LogoffRequests);
    }

    [Fact]
    public async Task ReusedIdInAnotherDomainIsNotSignedOutEvenWithBareUserConfiguration()
    {
        var reads = 0;
        var server = new FakeWtsServer([Session(1)], _ => ++reads == 1
            ? Alice()
            : new WindowsSessionUser("alice", "OTHER"));
        var manager = Create(SessionModeType.SignOut, server, "alice");

        var result = Assert.Single(await manager.ExecuteAsync());

        Assert.Equal(SessionStatusType.AlreadySignedOut, result.Status);
        Assert.Empty(server.LogoffRequests);
    }

    [Fact]
    public async Task SessionGoneBeforeLogoffIsAlreadySignedOut()
    {
        var reads = 0;
        var server = new FakeWtsServer([Session(1)], _ => ++reads == 1 ? Alice() : throw new Win32Exception(7022));

        var result = Assert.Single(await Create(SessionModeType.SignOut, server).ExecuteAsync());

        Assert.Equal(SessionStatusType.AlreadySignedOut, result.Status);
        Assert.Empty(server.LogoffRequests);
    }

    [Fact]
    public async Task VerificationOnlyQueriesTheTargetSession()
    {
        var reads = 0;
        var server = new FakeWtsServer([Session(1), Session(2)], id =>
        {
            if (id == 2)
            {
                throw new Win32Exception(5);
            }

            return ++reads <= 2 ? Alice() : throw new Win32Exception(7022);
        });

        var results = await Create(SessionModeType.SignOut, server).ExecuteAsync();

        Assert.Equal(new[] { SessionStatusType.Success, SessionStatusType.AccessDenied }, results.Select(result => result.Status));
        Assert.Equal(new[] { 1 }, server.LogoffRequests);
    }

    [Fact]
    public async Task VerificationAccessDeniedIsNotMistakenForSuccess()
    {
        var reads = 0;
        var server = new FakeWtsServer([Session(1)], _ => ++reads <= 2 ? Alice() : throw new Win32Exception(5));

        var result = Assert.Single(await Create(SessionModeType.SignOut, server).ExecuteAsync());

        Assert.Equal(SessionStatusType.NotConfirmed, result.Status);
        Assert.Contains("verification failed", result.Details);
    }

    [Fact]
    public async Task PersistingSessionIsNotConfirmed()
    {
        var server = new FakeWtsServer([Session(1)], _ => Alice());

        var result = Assert.Single(await Create(SessionModeType.SignOut, server).ExecuteAsync());

        Assert.Equal(SessionStatusType.NotConfirmed, result.Status);
    }

    private static WindowsSession Session(int id)
    {
        return new WindowsSession(id, $"RDP-Tcp#{id}", WtsConnectionStateType.Active);
    }

    private static WindowsSessionUser Alice()
    {
        return new WindowsSessionUser("alice", "DOMAIN");
    }

    private static SessionManager Create(SessionModeType mode, FakeWtsServer server)
    {
        return Create(mode, server, "DOMAIN\\alice");
    }

    private static SessionManager Create(SessionModeType mode, FakeWtsServer server, string userName)
    {
        var factory = new FakeWtsServerFactory(_ => server);
        return new SessionManager(new SessionSettings(mode, userName, ["VM"], TimeSpan.FromSeconds(30)),
            factory, new SessionSignOut(factory), NullLogger<SessionManager>.Instance);
    }
}
