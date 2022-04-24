namespace WebsocketClientLite.PCL.Model
{
    internal enum HandshakeStateKind
    {
        HandshakeSend,
        AwaitingHandshake,
        HandshakeSendFailed,        
        HandshakeFailed,
        HandshakeTimedOut,
        HandshakeCompletedSuccessfully,
    }
}
