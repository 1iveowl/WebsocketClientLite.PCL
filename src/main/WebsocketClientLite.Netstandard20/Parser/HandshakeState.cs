namespace WebsocketClientLite.PCL.Parser
{
    internal enum HandshakeState
    {
        Start,
        HandshakeSend,
        HandshakeSendFailed,
        HandshakeCompletedSuccessfully,
        HandshakeFailed,
        HandshakeTimedOut,
    }
}
