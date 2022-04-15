namespace WebsocketClientLite.PCL.Model
{
    internal enum WebsocketParserStateKind
    {
        Start,
        FirstByte,
        Mask,

        ReadingText,
        ReadingBinary,
        HasReadPayload
    }
}
