using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WebsocketClientLite.Helper;
using WebsocketClientLite.Service;
using WebsocketClientLite.VerifiedProtocol;
using Xunit;

namespace WebsocketClientLiteTest;

// Plain tests of the named boundary types (verification/core-boundary-types.md, S8).
// No socket, schedule or clock: each test enumerates the codes a value can hold and
// compares every wrapper with the generated method it forwards to.
public class CoreBoundaryTypesTests
{
    private static readonly byte[] ReceiveCodes = { 0, 1, 2, 8 };
    private static readonly byte[] TransmitCodes = { 0, 1, 2, 8, 15 };

    [Theory]
    [InlineData(typeof(ReceiveState))]
    [InlineData(typeof(TransmitState))]
    [InlineData(typeof(CloseState))]
    [InlineData(typeof(CloseEvent))]
    public void OnlyPrivateConstructorsAndOneByteField(Type type)
    {
        Assert.True(type.IsValueType && type.GetCustomAttributes(false)
            .Any(a => a.GetType().FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute"),
            type.Name + " is not a readonly struct");
        var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.True(constructors.Length == 1 && constructors[0].IsPrivate, type.Name + " has a non-private constructor");
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.True(fields.Length == 1 && fields[0].FieldType == typeof(byte) && fields[0].IsInitOnly,
            type.Name + " does not hold exactly one readonly byte");
        var conversions = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name is "op_Implicit" or "op_Explicit").ToArray();
        Assert.True(conversions.All(m => m.ReturnType != type), type.Name + " has a conversion into the type");
    }

    [Fact]
    public void DefaultIsCodeZeroAndNamedZeroInstances()
    {
        Assert.Equal(0, (byte)default(ReceiveState));
        Assert.Equal(0, (byte)ReceiveState.Idle);
        Assert.True(ReceiveState.Idle.IsIdle && !ReceiveState.Idle.IsClosed);
        Assert.Equal(0, default(TransmitState).Code);
        Assert.Equal(0, TransmitState.Idle.Code);
        Assert.Equal(0, default(CloseState).Code);
        Assert.Equal(0, CloseState.Open.Code);
        Assert.False(CloseState.Open.Started || CloseState.Open.ReceivedOrTerminal);
        Assert.Equal(new byte[] { 0, 1, 2 },
            new[] { CloseEvent.Send, CloseEvent.Received, CloseEvent.TimeoutOrFailure }.Select(e => e.Code).ToArray());
        Assert.Equal(0, default(CloseEvent).Code);
    }

    [Fact]
    public void FromCodeAcceptsExactlyTheReceiveStates()
    {
        for (int code = 0; code <= 255; code++)
        {
            if (ReceiveCodes.Contains((byte)code))
            {
                var state = ReceiveState.FromCode((byte)code);
                Assert.Equal(code, (byte)state);
                Assert.Equal(code == 0, state.IsIdle);
                Assert.Equal(code == 8, state.IsClosed);
            }
            else
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => ReceiveState.FromCode((byte)code));
            }
        }
    }

    [Fact]
    public async Task RawReadRejectsAnIllegalStateBeforeReadingAnyByte()
    {
        var stream = new MemoryStream(new byte[] { 0x82, 1, 7 });
        using var connection = new TcpConnectionService(() => false, (_, _, _, _) => true,
            (_, _) => Task.CompletedTask, (_, _) => { }, true, new TcpClient());
        typeof(TcpConnectionService).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(connection, stream);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => connection.ReadDataframeAsync(CancellationToken.None, 3));
        Assert.Equal(0, stream.Position);
        var frame = await connection.ReadDataframeAsync(CancellationToken.None, 0);
        Assert.True(frame is not null && frame.Payload!.SequenceEqual(new byte[] { 7 }), "Legal raw state did not read the frame");
    }

    [Fact]
    public void ReceiveStepMatchesStepAndReachesExactlyTheReceiveStates()
    {
        uint[] totals = { 0, 3, 8 };
        uint[] lengths = { 0, 2, 9 };
        var reached = new HashSet<byte> { 0 };
        var frontier = new Queue<ReceiveState>(new[] { ReceiveState.Idle });
        while (frontier.Count > 0)
        {
            var state = frontier.Dequeue();
            for (byte op = 0; op <= 15; op++)
            foreach (bool fin in new[] { false, true })
            foreach (uint total in totals)
            foreach (uint length in lengths)
            {
                ReceiveState.Step(state, op, fin, total, length, 8, out bool ok, out ReceiveState next, out uint size, out bool emit);
                Protocol.Step(state, op, fin, total, length, 8, out bool rawOk, out byte rawNext, out uint rawSize, out bool rawEmit);
                Assert.True(ok == rawOk && next == rawNext && size == rawSize && emit == rawEmit,
                    $"Step differs at state {(byte)state}, op {op}, fin {fin}, total {total}, length {length}");
                if (reached.Add(next)) frontier.Enqueue(next);
            }
        }
        Assert.Equal(ReceiveCodes, reached.OrderBy(c => c).ToArray());
    }

    [Fact]
    public void TransmitStepMatchesSendStepAndReachesExactlyTxState()
    {
        var reached = new HashSet<byte> { 0 };
        var frontier = new Queue<TransmitState>(new[] { TransmitState.Idle });
        while (frontier.Count > 0)
        {
            var state = frontier.Dequeue();
            var failed = TransmitState.SendFailed(state);
            Assert.Equal(Protocol.SendFailed(state.Code), failed.Code);
            if (reached.Add(failed.Code)) frontier.Enqueue(failed);
            for (byte op = 0; op <= 15; op++)
            foreach (bool fin in new[] { false, true })
            {
                TransmitState.SendStep(state, op, fin, out bool ok, out TransmitState next);
                Protocol.SendStep(state.Code, op, fin, out bool rawOk, out byte rawNext);
                Assert.True(ok == rawOk && next.Code == rawNext, $"SendStep differs at state {state.Code}, op {op}, fin {fin}");
                if (reached.Add(next.Code)) frontier.Enqueue(next);
            }
        }
        Assert.Equal(TransmitCodes, reached.OrderBy(c => c).ToArray());
        Assert.All(TransmitCodes, code => Assert.True(Protocol.TxState(code)));
    }

    [Fact]
    public void CloseStepMatchesCloseStepAndStaysWithinSevenBits()
    {
        var events = new[] { CloseEvent.Send, CloseEvent.Received, CloseEvent.TimeoutOrFailure };
        var reached = new HashSet<byte> { 0 };
        var frontier = new Queue<CloseState>(new[] { CloseState.Open });
        while (frontier.Count > 0)
        {
            var state = frontier.Dequeue();
            foreach (var closeEvent in events)
            {
                CloseState.Step(state, closeEvent, out CloseState next, out bool send, out bool release);
                Protocol.CloseStep(state.Code, closeEvent.Code, out byte rawNext, out bool rawSend, out bool rawRelease);
                Assert.True(next.Code == rawNext && send == rawSend && release == rawRelease,
                    $"CloseStep differs at state {state.Code}, event {closeEvent.Code}");
                Assert.Equal((next.Code & 6) != 0, next.ReceivedOrTerminal);
                Assert.Equal(next.Code != 0, next.Started);
                if (reached.Add(next.Code)) frontier.Enqueue(next);
            }
        }
        Assert.All(reached, code => Assert.True(code <= 7, "Close state above 7: " + code));
    }

    [Fact]
    public void WrappersDoNotAllocate()
    {
        static uint Run(int rounds)
        {
            uint sum = 0;
            for (int i = 0; i < rounds; i++)
            {
                ReceiveState.Step(ReceiveState.Idle, 2, false, 0, 4, 8, out _, out ReceiveState receive, out uint size, out _);
                ReceiveState.Step(receive, 0, true, size, 4, 8, out _, out receive, out size, out _);
                TransmitState.SendStep(TransmitState.Idle, 1, false, out _, out TransmitState transmit);
                transmit = TransmitState.SendFailed(transmit);
                CloseState.Step(CloseState.Open, CloseEvent.Send, out CloseState close, out _, out _);
                CloseState.Step(close, CloseEvent.Received, out close, out _, out _);
                sum += (byte)receive + size + transmit.Code + close.Code + (ReceiveState.FromCode(8).IsClosed ? 1u : 0u);
            }
            return sum;
        }
        Run(100);
        long before = GC.GetAllocatedBytesForCurrentThread();
        uint result = Run(10000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, "Wrappers allocated " + allocated + " bytes");
        Assert.Equal(10000u * (0 + 8 + 15 + 3 + 1), result);
    }
}
