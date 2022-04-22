using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IWebsocketClientLite.PCL
{
    /// <summary>
    /// Websocket Client Sender.
    /// </summary>
    public interface ISender
    {
        /// <summary>
        /// Send text message.
        /// </summary>
        /// <param name="message">Message</param>
        /// <param name="ct">Cancellation Token.</param>
        /// <returns></returns>
        Task SendText(string message, CancellationToken ct = default);

        /// <summary>
        /// Send list of text messages. Each message will be send as a fragment.
        /// </summary>
        /// <param name="messageList">List of messages.</param>
        /// <param name="ct">Cancellation Token.</param>
        /// <returns></returns>
        Task SendText(IEnumerable<string> messageList, CancellationToken ct = default);

        /// <summary>
        /// Send text message. Use this when you want to send multiple fragments but are unable to specify a list from the onset. When using this option you need to keep track of setting the first and last fragment and using continuation in the fragments in between.
        /// </summary>
        /// <param name="message">Message</param>
        /// <param name="opcode">Specific Websocket Opcode.</param>
        /// <param name="fragment">Specific Websocet Fragment.</param>
        /// <param name="ct">Cancellation Token.</param>
        /// <returns></returns>
        Task SendText(string message, OpcodeKind opcode, FragmentKind fragment = FragmentKind.None, CancellationToken ct = default);

        /// <summary>
        /// Send binary data.
        /// </summary>
        /// <param name="data">Binary data.</param>
        /// <param name="ct">Cancellation Token.</param>
        /// <returns></returns>
        Task SendBinary(byte[] data, CancellationToken ct = default);

        /// <summary>
        /// Send list of binary data. Each blob of data will be send as a fragment.
        /// </summary>
        /// <param name="dataList">List of binary data.</param>
        /// <param name="ct">Cancellation Token.</param>
        /// <returns></returns>
        Task SendBinary(IEnumerable<byte[]> dataList, CancellationToken ct = default);

        /// <summary>
        /// Send list of binary blobs. Use this when you want to send multiple fragments but are unable to specify a list from the onset. When using this option you need to keep track of setting the first and last fragment and using continuation in the fragments in between.
        /// </summary>
        /// <param name="data">Binary data.</param>
        /// <param name="opcode">Opcode.</param>
        /// <param name="fragment">Fragment.</param>
        /// <param name="ct">Cancellation Token.</param>
        /// <returns></returns>
        Task SendBinary(byte[] data, OpcodeKind opcode, FragmentKind fragment = FragmentKind.None, CancellationToken ct = default);

        /// <summary>
        /// Send a ping with text.
        /// </summary>
        /// <param name="message">Message send with ping. The default is null.</param>
        /// <param name="ct">Cancellation Token.</param>
        /// <returns></returns>
        Task SendPing(string message, CancellationToken ct = default);

        /// <summary>
        /// Send a ping with text.
        /// </summary>
        /// <param name="data">Data send with ping. The default is null.</param>
        /// <param name="ct">Cancellation Token.</param>
        /// <returns></returns>
        Task SendPing(byte[] data, CancellationToken ct = default);
    }
}
