namespace IWebsocketClientLite;

/// <summary>
/// Websocket Opcodes. For details see RFC 6544.
/// </summary>
public enum OpcodeKind
{
    /// <summary>
    /// Continuation frame.
    /// </summary>
    Continuation =  0x00,

    /// <summary>
    /// Text frame.
    /// </summary>
    Text =          0x01,

    /// <summary>
    /// Binary frame.
    /// </summary>
    Binary =        0x02,

    /// <summary>
    /// Frame reserved for further non-control frames.
    /// </summary>
    Reserved1 =     0x03,

    /// <summary>
    /// Frame reserved for further non-control frames.
    /// </summary>
    Reserved2 =     0x04,

    /// <summary>
    /// Frame reserved for further non-control frames.
    /// </summary>
    Reserved3 =     0x05,

    /// <summary>
    /// Frame reserved for further non-control frames.
    /// </summary>
    Reserved4 =     0x06,

    /// <summary>
    /// Frame reserved for further non-control frames.
    /// </summary>
    Reserved5 =     0x07,

    /// <summary>
    /// Close frame.
    /// </summary>
    Close =         0x08,

    /// <summary>
    /// Ping frame.
    /// </summary>
    Ping =          0x09,

    /// <summary>
    /// Pong frame.
    /// </summary>
    Pong =          0x0A,

    /// <summary>
    /// Frame reserved for further control frames.
    /// </summary>
    Reserved1a =    0x0B,

    /// <summary>
    /// Frame reserved for further control frames.
    /// </summary>
    Reserved2b =    0x0C,

    /// <summary>
    /// Frame reserved for further control frames.
    /// </summary>
    Reserved3c =    0x0D,

    /// <summary>
    /// Frame reserved for further control frames.
    /// </summary>
    Reserved4d =    0x0E,

    /// <summary>
    /// Frame reserved for further control frames.
    /// </summary>
    Reserved5e =    0x0F
}
