using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using WebsocketClientLite.Extension;
using Xunit;

namespace WebsocketClientLiteTest;

public class ObservableExtensionsTests
{
    [Fact]
    public async Task FinallyAsync_RunsAction_OnCompletion()
    {
        var ran = false;

        var result = await Observable.Return(42)
            .FinallyAsync(() => { ran = true; return Task.CompletedTask; });

        Assert.Equal(42, result);
        Assert.True(ran);
    }

    [Fact]
    public async Task FinallyAsync_RunsAction_OnError()
    {
        var ran = false;
        var source = Observable.Throw<int>(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source.FinallyAsync(() => { ran = true; return Task.CompletedTask; }));

        Assert.True(ran);
    }

    [Fact]
    public async Task FinallyAsync_PassesThroughValues()
    {
        var values = await Observable.Range(1, 3)
            .FinallyAsync(() => Task.CompletedTask)
            .ToArray();

        Assert.Equal(new[] { 1, 2, 3 }, values);
    }
}
