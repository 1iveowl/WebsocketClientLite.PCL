namespace IWebsocketClientLite.PCL
{
    public interface IDataframe
    {
        /// <summary>
        /// Text message. Only available when sending text messages.
        /// </summary>
        string? Message { get; }

        /// <summary>
        /// Binary data. Always available when data is send, including when sending text messages.
        /// </summary>
        byte[]? Binary { get; }
    }
}
