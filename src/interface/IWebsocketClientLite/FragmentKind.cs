namespace IWebsocketClientLite;

/// <summary>
/// Fragment kind
/// </summary>
public enum FragmentKind
{
    /// <summary>
    /// No fragment used.
    /// </summary>
    None =  0xff,

    /// <summary>
    /// First fragment in series of fragments.
    /// </summary>
    First = 0x00,

    /// <summary>
    /// Last fragment in series of fragments.
    /// </summary>
    Last =  0x80
}
