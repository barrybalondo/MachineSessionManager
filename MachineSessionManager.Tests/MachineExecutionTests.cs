using MachineSessionManager.Models;
using MachineSessionManager.Native;
using MachineSessionManager.Reporting;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using SessionManager = MachineSessionManager.Sessions.MachineSessionManager;

namespace MachineSessionManager.Tests;

public sealed class MachineExecutionTests
{
    [Fact]
    public async Task MachinesRunConcurrentlyButResultsAndReportsKeepConfigurationOrder()
    {
        using var releaseFirst = new ManualResetEventSlim();
        var firstStarted = Signal();
        var middleFinished = Signal();
        var lastFinished = Signal();
        var settings = new SessionSettings(SessionModeType.Check, "DOMAIN\\alice",
            ["Z-FIRST", "MIDDLE", "A-LAST"], TimeSpan.FromSeconds(30));
        var factory = new FakeWtsServerFactory(machine =>
        {
            if (machine == "Z-FIRST")
            {
                firstStarted.TrySetResult();
                Assert.True(releaseFirst.Wait(TimeSpan.FromSeconds(10)));
            }

            return new FakeWtsServer([Session(7), Session(2)], id =>
            {
                if (id == 2 && machine == "MIDDLE")
                {
                    middleFinished.TrySetResult();
                }
                else if (id == 2 && machine == "A-LAST")
                {
                    lastFinished.TrySetResult();
                }

                return Alice();
            });
        });
        var execution = Create(settings, factory).ExecuteAsync();
        try
        {
            await Task.WhenAll(firstStarted.Task, middleFinished.Task, lastFinished.Task)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(execution.IsCompleted);
        }
        finally
        {
            releaseFirst.Set();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var results = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "Z-FIRST", "Z-FIRST", "MIDDLE", "MIDDLE", "A-LAST", "A-LAST" },
            results.Select(result => result.Machine));
        Assert.Equal(new int?[] { 7, 2, 7, 2, 7, 2 }, results.Select(result => result.SessionId));
        Assert.Throws<NotSupportedException>(() => ((IList<SessionResult>)results).Clear());

        var report = new ReportBuilder(settings).Build(results);
        Assert.True(report.PlainTextBody.IndexOf("Z-FIRST", StringComparison.Ordinal) <
            report.PlainTextBody.IndexOf("MIDDLE", StringComparison.Ordinal));
        Assert.True(report.PlainTextBody.IndexOf("MIDDLE", StringComparison.Ordinal) <
            report.PlainTextBody.IndexOf("A-LAST", StringComparison.Ordinal));
        Assert.True(report.HtmlBody.IndexOf("Z-FIRST", StringComparison.Ordinal) <
            report.HtmlBody.IndexOf("MIDDLE", StringComparison.Ordinal));
        Assert.True(report.HtmlBody.IndexOf("MIDDLE", StringComparison.Ordinal) <
            report.HtmlBody.IndexOf("A-LAST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TimeoutRetainsCompletedSessionsAndDoesNotWaitForTheBlockedQuery()
    {
        using var release = new ManualResetEventSlim();
        var queryStarted = Signal();
        var disposed = Signal();
        var blocked = new FakeWtsServer([Session(1), Session(2)], id =>
        {
            if (id == 2)
            {
                queryStarted.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            }

            return Alice();
        }, dispose: () => disposed.TrySetResult());
        var factory = new FakeWtsServerFactory(machine => machine == "SLOW"
            ? blocked
            : new FakeWtsServer([Session(3)], _ => Alice()));
        var settings = new SessionSettings(SessionModeType.Check, "DOMAIN\\alice",
            ["SLOW", "FAST"], TimeSpan.FromSeconds(1));
        var execution = Create(settings, factory).ExecuteAsync();
        try
        {
            await queryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var results = await execution.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { "SLOW", "SLOW", "FAST" }, results.Select(result => result.Machine));
            Assert.Equal(new[] { SessionStatusType.SignedIn, SessionStatusType.TimedOut, SessionStatusType.SignedIn },
                results.Select(result => result.Status));
            Assert.Equal(new int?[] { 1, 2, 3 }, results.Select(result => result.SessionId));
            Assert.True(results[1].IsIssue);
            Assert.True(results[1].IsRelevant);
            Assert.False(disposed.Task.IsCompleted);

            release.Set();
            await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The late query must not replace the timeout with SignedIn.
            Assert.Equal(SessionStatusType.TimedOut, results[1].Status);
            Assert.Equal(3, results.Count);
        }
        finally
        {
            release.Set();
            await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ATimeoutDuringLogoffIsNotConfirmedAndDoesNotStartAnotherSession()
    {
        using var release = new ManualResetEventSlim();
        var logoffStarted = Signal();
        var signOutDisposed = Signal();
        var discoveryDisposed = Signal();
        var discovery = new FakeWtsServer([Session(1), Session(2)], _ => Alice(),
            dispose: () => discoveryDisposed.TrySetResult());
        var signOut = new FakeWtsServer([], _ => Alice(), logoff: _ =>
        {
            logoffStarted.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        }, dispose: () => signOutDisposed.TrySetResult());
        var opens = 0;
        var factory = new FakeWtsServerFactory(_ => Interlocked.Increment(ref opens) == 1 ? discovery : signOut);
        var settings = new SessionSettings(SessionModeType.SignOut, "DOMAIN\\alice", ["VM"], TimeSpan.FromSeconds(1));
        var execution = Create(settings, factory).ExecuteAsync();
        try
        {
            await logoffStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var results = await execution.WaitAsync(TimeSpan.FromSeconds(5));
            var result = Assert.Single(results);

            Assert.Equal(SessionStatusType.NotConfirmed, result.Status);
            Assert.Equal(1, result.SessionId);
            Assert.Equal("DOMAIN\\alice", result.UserName);
            Assert.True(result.IsIssue);
            Assert.False(signOutDisposed.Task.IsCompleted);

            release.Set();
            await Task.WhenAll(signOutDisposed.Task, discoveryDisposed.Task).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { 1 }, signOut.LogoffRequests);
            Assert.Equal(2, Volatile.Read(ref opens));
            Assert.Equal(SessionStatusType.NotConfirmed, result.Status);
        }
        finally
        {
            release.Set();
            await Task.WhenAll(signOutDisposed.Task, discoveryDisposed.Task).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ANewRunUsesFreshCancellationAndLateDiscoveryCannotLogOff()
    {
        using var release = new ManualResetEventSlim();
        var queryStarted = Signal();
        var disposed = Signal();
        var blocked = new FakeWtsServer([Session(1)], _ =>
        {
            queryStarted.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return Alice();
        }, dispose: () => disposed.TrySetResult());
        var opens = 0;
        var factory = new FakeWtsServerFactory(_ => Interlocked.Increment(ref opens) == 1
            ? blocked
            : new FakeWtsServer([Session(1)], _ => Alice(), protocol: _ => 0));
        var settings = new SessionSettings(SessionModeType.SignOut, "DOMAIN\\alice", ["VM"], TimeSpan.FromSeconds(1));
        var manager = Create(settings, factory);
        var execution = manager.ExecuteAsync();
        try
        {
            await queryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var first = Assert.Single(await execution.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(SessionStatusType.TimedOut, first.Status);

            var second = Assert.Single(await manager.ExecuteAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(SessionStatusType.Skipped, second.Status);

            release.Set();
            await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(blocked.LogoffRequests);
            Assert.Equal(SessionStatusType.TimedOut, first.Status);
        }
        finally
        {
            release.Set();
            await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task CancellationStopsWaitingForNativeWorkAndPreventsFurtherQueries()
    {
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var opened = Signal();
        var disposed = Signal();
        var reads = 0;
        var server = new FakeWtsServer([Session(1)], _ =>
        {
            Interlocked.Increment(ref reads);
            return Alice();
        }, dispose: () => disposed.TrySetResult());
        var factory = new FakeWtsServerFactory(_ =>
        {
            opened.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return server;
        });
        var settings = new SessionSettings(SessionModeType.SignOut, "DOMAIN\\alice", ["VM"], TimeSpan.FromSeconds(30));
        var execution = Create(settings, factory).ExecuteAsync(cancellation.Token);
        try
        {
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(disposed.Task.IsCompleted);

            release.Set();
            await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, Volatile.Read(ref reads));
            Assert.Empty(server.LogoffRequests);
        }
        finally
        {
            release.Set();
            await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task AnAlreadyCancelledRunDoesNotOpenAnyConnections()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var opens = 0;
        var factory = new FakeWtsServerFactory(_ =>
        {
            Interlocked.Increment(ref opens);
            return new FakeWtsServer([], _ => Alice());
        });
        var settings = new SessionSettings(SessionModeType.Check, "DOMAIN\\alice", ["VM"], TimeSpan.FromSeconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(settings, factory).ExecuteAsync(cancellation.Token));

        Assert.Equal(0, Volatile.Read(ref opens));
    }

    private static SessionManager Create(SessionSettings settings, IWtsServerFactory factory)
    {
        return new SessionManager(settings, factory, new SessionSignOut(factory), NullLogger<SessionManager>.Instance);
    }

    private static TaskCompletionSource Signal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static WindowsSession Session(int id)
    {
        return new WindowsSession(id, $"RDP-Tcp#{id}", WtsConnectionStateType.Active);
    }

    private static WindowsSessionUser Alice()
    {
        return new WindowsSessionUser("alice", "DOMAIN");
    }
}
