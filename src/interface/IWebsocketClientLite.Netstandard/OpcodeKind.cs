namespace IWebsocketClientLite.PCL
{
    public enum OpcodeKind
    {
        Continuation =  0x00,
        Text =          0x01,
        Binary =        0x02,
        Reserved1 =     0x03,
        Reserved2 =     0x04,
        Reserved3 =     0x05,
        Reserved4 =     0x06,
        Reserved5 =     0x07,
        Close =         0x08,
        Ping =          0x09,
        Pong =          0x0A,
        Reserved1a =    0x0B,
        Reserved2b =    0x0C,
        Reserved3c =    0x0D,
        Reserved4d =    0x0E,
        Reserved5e =    0x0F
    }
}
