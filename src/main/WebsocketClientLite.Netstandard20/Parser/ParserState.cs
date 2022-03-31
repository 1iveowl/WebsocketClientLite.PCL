namespace WebsocketClientLite.PCL.Parser
{
    internal enum ParserState
    {
        Start,
        HandshakeCompletedSuccessfully,
        HandshakeFailed,
        HandshakeTimedOut,
    }
}
