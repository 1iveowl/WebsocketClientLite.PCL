using System;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite;
using Xunit;

namespace WebsocketClientLiteTest;

/// <summary>
/// Stress-loop for the error-delivery ordering contract: a read-loop error must
/// reach the subscriber as OnError on every run, never swallowed into a
/// graceful completion by the racing teardown. The subscriber's tuple stream is
/// fed by two independent Rx branches (status and dataframes); before errors
/// were routed through the status channel (Aborted), the close handshake's
/// Disconnected/OnCompleted could win the race and terminate the stream first —
/// at roughly 1-in-300 per connection under parallel test load, which is why
/// this loops.
/// </summary>
public class ErrorDeliveryStressTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task ReadLoopError_AlwaysReachesSubscriber_UnderRepeatedRuns()
    {
        for (int i = 0; i < 200; i++)
        {
            using var server = new LoopbackWebSocketServer();
            server.Start(async (stream, ct) =>
            {
                await LoopbackWebSocketServer.WriteFrameAsync(stream, 0x1, new byte[64], true, ct);
                await Task.Delay(System.Threading.Timeout.Infinite, ct);
            });

            using var client = new ClientWebSocketRx { MaxFrameSize = 16 };
            var terminal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using var subscription = client.WebsocketConnectWithStatusObservable(server.Uri)
                .Subscribe(
                    tuple =>
                    {
                        if (tuple.state == ConnectionStatus.WebsocketConnected)
                        {
                            connected.TrySetResult();
                        }
                    },
                    ex => terminal.TrySetResult($"error: {ex.Message}"),
                    () => terminal.TrySetResult("completed"));

            await connected.Task.WaitAsync(Timeout);
            var kind = await terminal.Task.WaitAsync(Timeout);

            Assert.True(
                kind.StartsWith("error", StringComparison.Ordinal),
                $"iteration {i}: stream terminated with '{kind}' instead of the read-loop error");
        }
    }
}
