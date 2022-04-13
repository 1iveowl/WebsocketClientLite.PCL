namespace IWebsocketClientLite.PCL
{
    public enum FrameTypeKind
    {
        Continuation = 0,
        Text = 129,
        Binary = 130,
        FirstOfMultipleFrames = 1,
        LastInMultipleFrames = 128,
        Single = 129,
        Ping = 137,
        Pong = 138,
        Close = 136,
        None = 255
    }
}
