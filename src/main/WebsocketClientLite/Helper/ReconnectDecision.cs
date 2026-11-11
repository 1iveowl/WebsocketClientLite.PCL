using System;

namespace WebsocketClientLite.Helper;

// The lifecycle decision of ClientWebSocketRx (P3-D, verification/reconnect-intent.md)
// as one pure function. The transition table it implements, its undecided cases and
// its tests are in verification/reconnect-decision.md. The caller keeps the lock and
// the state, stores the returned state and performs the returned effect.

/// <summary>An event the lifecycle decision is asked about.</summary>
internal enum LifecycleEvent
{
    /// <summary>A subscription asks to be admitted.</summary>
    Subscribe,

    /// <summary>An admitted subscription asks to create and register its service.</summary>
    Register,

    /// <summary>A subscription's handshake succeeded and asks to install its sender.</summary>
    Connect,

    /// <summary>A subscription's status observable ended.</summary>
    Terminate,

    /// <summary>The client is disposed, synchronously or asynchronously.</summary>
    Dispose,
}

/// <summary>What the caller does after a decision.</summary>
internal enum LifecycleEffect
{
    /// <summary>No field and no observable changes.</summary>
    Nothing,

    /// <summary>Continue building the subscription.</summary>
    Admit,

    /// <summary>Create the service and add it to the active connections.</summary>
    Register,

    /// <summary>Set the sender and the negotiated subprotocols, and report connected.</summary>
    Install,

    /// <summary>Fail with <see cref="ObjectDisposedException"/>.</summary>
    Reject,

    /// <summary>Report not connected.</summary>
    NotifyNotConnected,

    /// <summary>Admission is closed; the caller drains the active connections.</summary>
    CloseAdmission,
}

/// <summary>
/// The state the decision reads: whether the client is disposed, and the identity of
/// the most recently admitted subscription (null before the first). The default value
/// is the state of a new client.
/// </summary>
internal readonly record struct LifecycleState(bool Disposed, object? Generation);

/// <summary>The state after an event, and the effect the caller performs.</summary>
internal readonly record struct LifecycleStep(LifecycleState Next, LifecycleEffect Effect);

internal static class ReconnectDecision
{
    /// <summary>Decides one lifecycle event. Pure: no fields, no locks, no I/O.</summary>
    /// <param name="state">The current state.</param>
    /// <param name="lifecycleEvent">The event.</param>
    /// <param name="subscription">
    /// The identity of the subscription that raises the event. Every subscription
    /// passes its own non-null identity. <see cref="LifecycleEvent.Dispose"/> is not
    /// raised by a subscription and passes null.
    /// </param>
    internal static LifecycleStep Decide(LifecycleState state, LifecycleEvent lifecycleEvent, object? subscription) =>
        lifecycleEvent switch
        {
            // Rows 1 to 6. Disposal is permanent; otherwise the newest subscription
            // owns the final not-connected notification.
            LifecycleEvent.Subscribe when state.Disposed => new(state, LifecycleEffect.Reject),
            LifecycleEvent.Subscribe => new(state with { Generation = subscription }, LifecycleEffect.Admit),
            // Rows 7 to 18. Register and Connect look only at disposal.
            LifecycleEvent.Register => new(state, state.Disposed ? LifecycleEffect.Reject : LifecycleEffect.Register),
            LifecycleEvent.Connect => new(state, state.Disposed ? LifecycleEffect.Reject : LifecycleEffect.Install),
            // Rows 19 to 24. Only the newest subscription reports not connected, also after disposal.
            LifecycleEvent.Terminate => new(state,
                ReferenceEquals(state.Generation, subscription) ? LifecycleEffect.NotifyNotConnected : LifecycleEffect.Nothing),
            // Rows 25 to 30. Dispose closes admission once; later calls change nothing.
            LifecycleEvent.Dispose when state.Disposed => new(state, LifecycleEffect.Nothing),
            LifecycleEvent.Dispose => new(state with { Disposed = true }, LifecycleEffect.CloseAdmission),
            _ => throw new ArgumentOutOfRangeException(nameof(lifecycleEvent), lifecycleEvent, null),
        };
}
