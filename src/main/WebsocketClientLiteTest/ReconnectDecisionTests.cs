using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WebsocketClientLite.Helper;
using Xunit;

namespace WebsocketClientLiteTest;

// Plain tests of the pure lifecycle decision, ReconnectDecision.Decide. No schedule,
// no socket, no clock. The rows are the transition table in
// verification/reconnect-decision.md; the shell around the function is tested by
// ReconnectTests.
public class ReconnectDecisionTests
{
    // The subscription that raises the event, and another one.
    private static readonly object G = new();
    private static readonly object X = new();

    private static readonly string[] StateNames = ["O0", "Og", "Ox", "D0", "Dg", "Dx"];

    private static LifecycleState State(string name) => name switch
    {
        "O0" => new(false, null),
        "Og" => new(false, G),
        "Ox" => new(false, X),
        "D0" => new(true, null),
        "Dg" => new(true, G),
        "Dx" => new(true, X),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    // Rows 2, 5, 7, 10, 13, 16, 19 and 22 are unreachable; they record what the code
    // does so that the function stays total. Rows 3, 9, 12, 15, 21 and 24 include the
    // undecided case U1, rows 14 and 15 U2, and row 23 U3: they pin today's behaviour,
    // not a decided intent.
    [Theory]
    [InlineData(1, "Subscribe", "O0", "Og", "Admit")]
    [InlineData(2, "Subscribe", "Og", "Og", "Admit")]
    [InlineData(3, "Subscribe", "Ox", "Og", "Admit")]
    [InlineData(4, "Subscribe", "D0", "D0", "Reject")]
    [InlineData(5, "Subscribe", "Dg", "Dg", "Reject")]
    [InlineData(6, "Subscribe", "Dx", "Dx", "Reject")]
    [InlineData(7, "Register", "O0", "O0", "Register")]
    [InlineData(8, "Register", "Og", "Og", "Register")]
    [InlineData(9, "Register", "Ox", "Ox", "Register")]
    [InlineData(10, "Register", "D0", "D0", "Reject")]
    [InlineData(11, "Register", "Dg", "Dg", "Reject")]
    [InlineData(12, "Register", "Dx", "Dx", "Reject")]
    [InlineData(13, "Connect", "O0", "O0", "Install")]
    [InlineData(14, "Connect", "Og", "Og", "Install")]
    [InlineData(15, "Connect", "Ox", "Ox", "Install")]
    [InlineData(16, "Connect", "D0", "D0", "Reject")]
    [InlineData(17, "Connect", "Dg", "Dg", "Reject")]
    [InlineData(18, "Connect", "Dx", "Dx", "Reject")]
    [InlineData(19, "Terminate", "O0", "O0", "Nothing")]
    [InlineData(20, "Terminate", "Og", "Og", "NotifyNotConnected")]
    [InlineData(21, "Terminate", "Ox", "Ox", "Nothing")]
    [InlineData(22, "Terminate", "D0", "D0", "Nothing")]
    [InlineData(23, "Terminate", "Dg", "Dg", "NotifyNotConnected")]
    [InlineData(24, "Terminate", "Dx", "Dx", "Nothing")]
    [InlineData(25, "Dispose", "O0", "D0", "CloseAdmission")]
    [InlineData(26, "Dispose", "Og", "Dg", "CloseAdmission")]
    [InlineData(27, "Dispose", "Ox", "Dx", "CloseAdmission")]
    [InlineData(28, "Dispose", "D0", "D0", "Nothing")]
    [InlineData(29, "Dispose", "Dg", "Dg", "Nothing")]
    [InlineData(30, "Dispose", "Dx", "Dx", "Nothing")]
    public void EveryStateAndEvent(int row, string lifecycleEvent, string state, string next, string effect)
    {
        var e = Enum.Parse<LifecycleEvent>(lifecycleEvent);
        // Dispose is not raised by a subscription; every other event comes from G.
        var step = ReconnectDecision.Decide(State(state), e, e == LifecycleEvent.Dispose ? null : G);
        Assert.True(State(next) == step.Next, $"Row {row}: next state is not {next}");
        Assert.Equal(Enum.Parse<LifecycleEffect>(effect), step.Effect);
    }

    [Fact]
    public void TableHasOneRowPerStateAndEvent()
    {
        var rows = typeof(ReconnectDecisionTests).GetMethod(nameof(EveryStateAndEvent))!
            .GetCustomAttributes<InlineDataAttribute>()
            .Select(a => a.GetData(null!).Single())
            .OrderBy(r => (int)r[0])
            .ToArray();
        var events = Enum.GetNames<LifecycleEvent>();
        Assert.Equal(events.Length * StateNames.Length, rows.Length);
        for (int i = 0; i < rows.Length; i++)
        {
            // Rows are numbered as in the table: six states per event, in event order.
            Assert.Equal(i + 1, (int)rows[i][0]);
            Assert.Equal(events[i / StateNames.Length], (string)rows[i][1]);
            Assert.Equal(StateNames[i % StateNames.Length], (string)rows[i][2]);
        }
        Assert.Equal(Enum.GetNames<LifecycleEffect>().Order(), rows.Select(r => (string)r[4]).Distinct().Order());
    }

    [Fact]
    public void UndefinedEventIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ReconnectDecision.Decide(default, (LifecycleEvent)5, G));

    [Fact]
    public void NewClientIsOpenWithNoSubscription() =>
        Assert.True(default(LifecycleState) == State("O0"));

    // Folds events from a state and returns the final state, the effects and
    // IsConnected as the shell reports it (Install reports true, NotifyNotConnected
    // false).
    private static (LifecycleState State, LifecycleEffect[] Effects, bool Connected) Run(
        LifecycleState state, bool connected, params (LifecycleEvent Event, object? Subscription)[] events)
    {
        var effects = new List<LifecycleEffect>();
        foreach (var (e, subscription) in events)
        {
            var step = ReconnectDecision.Decide(state, e, subscription);
            state = step.Next;
            effects.Add(step.Effect);
            if (step.Effect == LifecycleEffect.Install) connected = true;
            if (step.Effect == LifecycleEffect.NotifyNotConnected) connected = false;
        }
        return (state, effects.ToArray(), connected);
    }

    // P3-D counterexample 1, admission after disposal: Dispose, then Subscribe(g1).
    // DisposeAsync reaches the decision twice, once itself and once through Dispose(true).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposedClientRejectsAdmissionAsEvents(bool asynchronous)
    {
        var g1 = new object();
        var events = new List<(LifecycleEvent, object?)> { (LifecycleEvent.Dispose, null) };
        if (asynchronous) events.Add((LifecycleEvent.Dispose, null));
        events.Add((LifecycleEvent.Subscribe, g1));
        var (state, effects, connected) = Run(default, false, [.. events]);
        var expected = asynchronous
            ? new[] { LifecycleEffect.CloseAdmission, LifecycleEffect.Nothing, LifecycleEffect.Reject }
            : new[] { LifecycleEffect.CloseAdmission, LifecycleEffect.Reject };
        Assert.Equal(expected, effects);
        Assert.True(new LifecycleState(true, null) == state, "Rejected subscription changed the state");
        Assert.False(connected);
    }

    // P3-D counterexample 2, stale final notification: g1 connects and completes; its
    // terminal callback subscribes g2, which connects before the callback returns;
    // then g1's teardown runs. At 6709c99 that last step reported not connected.
    [Fact]
    public void TerminalCallbackSuccessorSurvivesOldCleanupAsEvents()
    {
        var g1 = new object();
        var g2 = new object();
        var (state, effects, connected) = Run(default, false,
            (LifecycleEvent.Subscribe, g1), (LifecycleEvent.Register, g1), (LifecycleEvent.Connect, g1),
            (LifecycleEvent.Subscribe, g2), (LifecycleEvent.Register, g2), (LifecycleEvent.Connect, g2),
            (LifecycleEvent.Terminate, g1));
        Assert.Equal(new[]
        {
            LifecycleEffect.Admit, LifecycleEffect.Register, LifecycleEffect.Install,
            LifecycleEffect.Admit, LifecycleEffect.Register, LifecycleEffect.Install,
            LifecycleEffect.Nothing,
        }, effects);
        Assert.True(connected, "Old cleanup reset successor connectivity");
        Assert.True(new LifecycleState(false, g2) == state, "Successor does not own the final notification");

        // The successor's own teardown still reports not connected, and disposal closes admission.
        var (end, tail, after) = Run(state, connected, (LifecycleEvent.Terminate, g2), (LifecycleEvent.Dispose, null));
        Assert.Equal(new[] { LifecycleEffect.NotifyNotConnected, LifecycleEffect.CloseAdmission }, tail);
        Assert.False(after);
        Assert.True(new LifecycleState(true, g2) == end, "Disposal did not close admission");
    }
}
