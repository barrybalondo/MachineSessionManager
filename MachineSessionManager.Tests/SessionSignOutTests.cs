using System.ComponentModel;
using MachineSessionManager.Native;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using SessionManager = MachineSessionManager.Sessions.MachineSessionManager;

namespace MachineSessionManager.Tests;

public sealed class SessionSignOutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlyReturnsDisposeTheOwnedConnection(bool ownershipChanged)
    {
        string? openedMachine = null;
        var disposals = 0;
        var server = new FakeWtsServer([], _ => ownershipChanged
            ? new WindowsSessionUser("other", "DOMAIN")
            : Alice(), protocol: _ => 0, dispose: () => disposals++);
        var factory = new FakeWtsServerFactory(machine =>
        {
            openedMachine = machine;
            return server;
        });

        var result = await new SessionSignOut(factory).ExecuteAsync("VM", Session(), Alice());

        Assert.Equal("VM", openedMachine);
        Assert.Equal(ownershipChanged ? SessionStatusType.AlreadySignedOut : SessionStatusType.Skipped, result.Status);
        Assert.Equal(1, disposals);
        Assert.Empty(server.LogoffRequests);
    }

    [Fact]
    public async Task QueryFailureDisposesTheOwnedConnection()
    {
        var disposed = false;
        var server = new FakeWtsServer([], _ => throw new Win32Exception(5), dispose: () => disposed = true);
        var signOut = new SessionSignOut(new FakeWtsServerFactory(_ => server));

        await Assert.ThrowsAsync<Win32Exception>(() => signOut.ExecuteAsync("VM", Session(), Alice()));

        Assert.True(disposed);
    }

    [Fact]
    public async Task ConnectionStaysOpenThroughVerificationAndIsThenDisposed()
    {
        var reads = 0;
        var disposed = false;
        var server = new FakeWtsServer([], _ =>
        {
            Assert.False(disposed);
            return ++reads == 1 ? Alice() : throw new Win32Exception(7022);
        }, logoff: _ => Assert.False(disposed), dispose: () => disposed = true);
        var signOut = new SessionSignOut(new FakeWtsServerFactory(_ => server));

        var result = await signOut.ExecuteAsync("VM", Session(), Alice());

        Assert.Equal(SessionStatusType.Success, result.Status);
        Assert.Equal(new[] { 1 }, server.LogoffRequests);
        Assert.True(disposed);
    }

    [Fact]
    public async Task UnrequestedCancellationDuringVerificationIsNotConfirmed()
    {
        var reads = 0;
        var server = new FakeWtsServer([], _ => ++reads == 1
            ? Alice()
            : throw new OperationCanceledException("Verification cancelled without a job cancellation."));
        var signOut = new SessionSignOut(new FakeWtsServerFactory(_ => server));

        var result = await signOut.ExecuteAsync("VM", Session(), Alice());

        Assert.Equal(SessionStatusType.NotConfirmed, result.Status);
        Assert.Contains("verification failed", result.Details);
        Assert.Equal(new[] { 1 }, server.LogoffRequests);
    }

    [Fact]
    public async Task CancellationDuringVerificationDisposesTheOwnedConnection()
    {
        using var cancellation = new CancellationTokenSource();
        var disposed = false;
        var server = new FakeWtsServer([], _ => Alice(), logoff: _ => cancellation.Cancel(),
            dispose: () => disposed = true);
        var signOut = new SessionSignOut(new FakeWtsServerFactory(_ => server));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            signOut.ExecuteAsync("VM", Session(), Alice(), cancellation.Token));

        Assert.True(disposed);
    }

    [Fact]
    public async Task AlreadyCancelledOperationDoesNotOpenAConnection()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var opened = false;
        var factory = new FakeWtsServerFactory(_ =>
        {
            opened = true;
            throw new InvalidOperationException("A cancelled operation must not open a connection.");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SessionSignOut(factory).ExecuteAsync("VM", Session(), Alice(), cancellation.Token));

        Assert.False(opened);
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("User")]
    [InlineData("Protocol")]
    public async Task CancellationBetweenNativeCallsPreventsLogoff(string cancelDuring)
    {
        using var cancellation = new CancellationTokenSource();
        var userReads = 0;
        var protocolReads = 0;
        var disposed = false;
        var server = new FakeWtsServer([], _ =>
        {
            userReads++;
            if (cancelDuring == "User")
            {
                cancellation.Cancel();
            }

            return Alice();
        }, protocol: _ =>
        {
            protocolReads++;
            if (cancelDuring == "Protocol")
            {
                cancellation.Cancel();
            }

            return 2;
        }, dispose: () => disposed = true);
        var factory = new FakeWtsServerFactory(_ =>
        {
            if (cancelDuring == "Open")
            {
                cancellation.Cancel();
            }

            return server;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SessionSignOut(factory).ExecuteAsync("VM", Session(), Alice(), cancellation.Token));

        Assert.Empty(server.LogoffRequests);
        Assert.True(disposed);
        Assert.Equal(cancelDuring == "Open" ? 0 : 1, userReads);
        Assert.Equal(cancelDuring == "Protocol" ? 1 : 0, protocolReads);
    }

    [Fact]
    public async Task ReusedSignOutObjectOpensASeparateConnectionForEachMachine()
    {
        var machines = new List<string>();
        var connections = new List<FakeWtsServer>();
        var disposals = 0;
        var factory = new FakeWtsServerFactory(machine =>
        {
            machines.Add(machine);
            var server = new FakeWtsServer([], _ => Alice(), protocol: _ => 0, dispose: () => disposals++);
            connections.Add(server);
            return server;
        });
        var signOut = new SessionSignOut(factory);

        await signOut.ExecuteAsync("FIRST", Session(), Alice());
        await signOut.ExecuteAsync("SECOND", Session(), Alice());

        Assert.Equal(new[] { "FIRST", "SECOND" }, machines);
        Assert.NotSame(connections[0], connections[1]);
        Assert.Equal(2, disposals);
    }

    [Fact]
    public async Task FailedSignOutConnectionDoesNotStopOtherSessionChecks()
    {
        var discovery = new FakeWtsServer([Session(), new WindowsSession(2, "RDP-Tcp#2", WtsConnectionStateType.Active)], _ => Alice());
        var signOutDisposed = false;
        var signOutServer = new FakeWtsServer([], _ => Alice(), protocol: _ => 0, dispose: () => signOutDisposed = true);
        var opens = 0;
        var factory = new FakeWtsServerFactory(_ => ++opens switch
        {
            1 => discovery,
            2 => throw new Win32Exception(5),
            _ => signOutServer
        });
        var manager = new SessionManager(new SessionSettings(SessionModeType.SignOut, "DOMAIN\\alice",
            ["VM"], TimeSpan.FromSeconds(30)), factory, new SessionSignOut(factory),
            NullLogger<SessionManager>.Instance);

        var results = await manager.ExecuteAsync();

        Assert.Equal(new[] { SessionStatusType.AccessDenied, SessionStatusType.Skipped }, results.Select(result => result.Status));
        Assert.Equal(new int?[] { 1, 2 }, results.Select(result => result.SessionId));
        Assert.Equal(3, opens);
        Assert.True(signOutDisposed);
    }

    private static WindowsSession Session()
    {
        return new WindowsSession(1, "RDP-Tcp#1", WtsConnectionStateType.Active);
    }

    private static WindowsSessionUser Alice()
    {
        return new WindowsSessionUser("alice", "DOMAIN");
    }
}
