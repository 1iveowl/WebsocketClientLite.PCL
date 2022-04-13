namespace IWebsocketClientLite.PCL
{
    internal enum HandshakeStateKind
    {
        Start,
        IsListeningForHandShake,
        IsListening,
        IsParsing,
        MessageReceived,
        Exiting
    }
}
