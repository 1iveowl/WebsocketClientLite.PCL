namespace WebsocketClientLite.PCL.Model
{
    internal enum HandshakeState
    {
        Start = 0,
        HandshakeSend = 1,
        HandshakeSendFailed = 2,        
        HandshakeFailed = 3,
        HandshakeTimedOut = 4,
        HandshakeCompletedSuccessfully = 5,
    }
}
