namespace WebsocketClientLite.Model;

internal enum HandshakeStateKind
{
    HandshakeSend,
    AwaitingHandshake,
    HandshakeSendFailed,        
    HandshakeFailed,
    HandshakeTimedOut,
    HandshakeCompletedSuccessfully,
}
