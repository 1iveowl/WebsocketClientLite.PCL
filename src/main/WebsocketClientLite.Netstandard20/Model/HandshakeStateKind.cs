namespace WebsocketClientLite.PCL.Model
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
