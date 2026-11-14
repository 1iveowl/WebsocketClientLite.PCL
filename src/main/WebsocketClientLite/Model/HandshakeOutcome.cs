using WebsocketClientLite.CustomException;

namespace WebsocketClientLite.Model;

/// <summary>The server accepted the opening handshake.</summary>
internal sealed class HandshakeAccepted
{
    internal static HandshakeAccepted Instance { get; } = new();

    private HandshakeAccepted() { }
}

/// <summary>The opening handshake did not complete: the request could not be sent, the
/// response was rejected or the timeout passed. <see cref="Error"/> says which.</summary>
internal sealed record class HandshakeFailed(WebsocketClientLiteException Error);

/// <summary>The result of the opening handshake. A failure always carries its exception and
/// an acceptance never has one, so the connect path cannot meet a state with neither.
/// <c>default(HandshakeOutcome)</c> is no outcome and matches no case; nothing creates it.</summary>
internal union HandshakeOutcome(HandshakeAccepted, HandshakeFailed);
