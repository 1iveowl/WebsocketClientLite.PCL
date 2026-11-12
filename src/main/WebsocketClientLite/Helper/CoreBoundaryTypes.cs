using System;
using Protocol = WebsocketClientLite.VerifiedProtocol.Protocol;

namespace WebsocketClientLite.Helper;

// Named types for the state and event codes the verified core takes as raw bytes
// (verification/core-boundary-types.md). Compiled Dafny does not check `requires` at
// run time, so each type makes its precondition's value range unrepresentable: the
// constructor is private, a value comes only from a named instance, from a checked
// conversion or from a verified method's ensured output, and default(T) is code 0,
// which every state range below contains. default(CloseEvent) is no event: its Code
// throws. Each type has one shape: private readonly byte _code and internal byte Code.
// There is no conversion into or out of a type, so leaving one is always an explicit
// .Code. Each wrapper only forwards to the generated method.

/// <summary>Receive message state of <c>Step</c> and <c>Admit</c>: 0 idle, 1 text
/// message, 2 binary message, 8 Close received (<c>Protocol.dfy</c>, <c>Step</c>).</summary>
internal readonly struct ReceiveState
{
    private readonly byte _code;

    private ReceiveState(byte code) => _code = code;

    internal static ReceiveState Idle => default;

    internal byte Code => _code;

    internal bool IsIdle => _code == 0;

    internal bool IsClosed => _code == 8;

    /// <summary>Checked conversion for callers that hold a raw code. Throws for every
    /// byte outside {0, 1, 2, 8}.</summary>
    internal static ReceiveState FromCode(byte code) => code is 0 or 1 or 2 or 8
        ? new ReceiveState(code)
        : throw new ArgumentOutOfRangeException(nameof(code), code, "Not a receive state.");

    /// <summary><c>Protocol.Step</c> with a typed state. Step ensures that <paramref name="next"/>
    /// is in {0, 1, 2, 8} when <paramref name="ok"/> and equals <paramref name="state"/> otherwise.</summary>
    internal static void Step(ReceiveState state, byte op, bool fin, uint total, uint length, uint limit,
        out bool ok, out ReceiveState next, out uint size, out bool emit)
    {
        Protocol.Step(state._code, op, fin, total, length, limit, out ok, out byte code, out size, out emit);
        next = new ReceiveState(code);
    }
}

/// <summary>Transmit state of <c>SendStep</c> and <c>SendFailed</c>: 0 idle, 1 text
/// message, 2 binary message, 8 Close sent, 15 failed (<c>Protocol.dfy</c>, <c>TxState</c>).</summary>
internal readonly struct TransmitState
{
    private readonly byte _code;

    private TransmitState(byte code) => _code = code;

    internal static TransmitState Idle => default;

    internal byte Code => _code;

    /// <summary><c>Protocol.SendStep</c> with a typed state. SendStep requires and ensures
    /// <c>TxState</c>.</summary>
    internal static void SendStep(TransmitState state, byte op, bool fin, out bool ok, out TransmitState next)
    {
        Protocol.SendStep(state._code, op, fin, out ok, out byte code);
        next = new TransmitState(code);
    }

    /// <summary><c>Protocol.SendFailed</c>, which returns 8 or 15.</summary>
    internal static TransmitState SendFailed(TransmitState state) => new(Protocol.SendFailed(state._code));
}

/// <summary>Close state bits of <c>CloseStep</c>: sent 1, received 2, terminal timeout
/// or failure 4, so 0..7 (<c>Protocol.dfy</c>, <c>CloseStep</c>).</summary>
internal readonly struct CloseState
{
    private readonly byte _code;

    private CloseState(byte code) => _code = code;

    internal static CloseState Open => default;

    internal byte Code => _code;

    /// <summary>A Close was received or the close ended on a timeout or failure.</summary>
    internal bool ReceivedOrTerminal => (_code & 6) != 0;

    /// <summary>Any close bit is set.</summary>
    internal bool Started => _code != 0;

    /// <summary><c>Protocol.CloseStep</c> with a typed state and event. CloseStep requires
    /// <c>state &lt;= 7 &amp;&amp; event &lt;= 2</c> and ensures <c>next &lt;= 7</c>.</summary>
    internal static void Step(CloseState state, CloseEvent closeEvent, out CloseState next, out bool send, out bool release)
    {
        Protocol.CloseStep(state._code, closeEvent.Code, out byte code, out send, out release);
        next = new CloseState(code);
    }
}

/// <summary>Event of <c>CloseStep</c>: 0 request to send Close, 1 Close received,
/// 2 timeout or transport failure. The field holds the code plus one, so that
/// default(CloseEvent) is none of the three events: its <see cref="Code"/> throws, and
/// with it <see cref="CloseState.Step"/>.</summary>
internal readonly struct CloseEvent
{
    private readonly byte _code;

    private CloseEvent(byte code) => _code = (byte)(code + 1);

    internal byte Code => _code != 0
        ? (byte)(_code - 1)
        : throw new InvalidOperationException("default(CloseEvent) is not a close event.");

    internal static CloseEvent Send => new(0);

    internal static CloseEvent Received => new(1);

    internal static CloseEvent TimeoutOrFailure => new(2);
}
