namespace IWebsocketClientLite.PCL
{
    public enum FrameType
    {
        Single,
        FirstOfMultipleFrames,
        Continuation,
        LastInMultipleFrames,
        CloseControlFrame,
    }
}
