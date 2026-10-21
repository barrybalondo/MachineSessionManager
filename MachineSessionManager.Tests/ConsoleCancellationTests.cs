using Xunit;

namespace MachineSessionManager.Tests;

public sealed class ConsoleCancellationTests
{
    [Fact]
    public void Cancel_RequestsCancellation()
    {
        using var cancellation = new ConsoleCancellation();

        cancellation.Cancel();

        Assert.True(cancellation.Token.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_AfterDisposal_IsIgnored()
    {
        var cancellation = new ConsoleCancellation();
        cancellation.Dispose();

        cancellation.Cancel();
        cancellation.Dispose();

        Assert.False(cancellation.Token.IsCancellationRequested);
    }

    [Fact]
    public void Dispose_FromCancellationCallback_DefersSourceDisposal()
    {
        using var cancellation = new ConsoleCancellation();
        var token = cancellation.Token;
        using var registration = token.Register(() =>
        {
            cancellation.Dispose();
            Assert.True(token.WaitHandle.WaitOne(0));
        });

        cancellation.Cancel();

        Assert.Throws<ObjectDisposedException>(() => token.WaitHandle);
    }

    [Fact]
    public async Task Dispose_WhileCancellationCallbackIsRunning_DefersSourceDisposal()
    {
        using var cancellation = new ConsoleCancellation();
        var token = cancellation.Token;
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var finishCallback = new ManualResetEventSlim();
        using var registration = token.Register(() =>
        {
            callbackStarted.SetResult();
            Assert.True(finishCallback.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(token.WaitHandle.WaitOne(0));
        });

        var cancelling = Task.Run(cancellation.Cancel);
        try
        {
            await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Dispose();
            cancellation.Cancel();
        }
        finally
        {
            finishCallback.Set();
            await cancelling.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Throws<ObjectDisposedException>(() => token.WaitHandle);
    }
}
