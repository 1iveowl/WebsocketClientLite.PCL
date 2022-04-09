namespace IWebsocketClientLite.PCL
{
    public enum FrameType
    {
        Continuation = 0,
        FirstOfMultipleFrames = 1,
        LastInMultipleFrames = 128,
        Single = 129,
        CloseControlFrame = 136
    }
}
