using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using IWebsocketClientLite;
using WebsocketClientLite.Helper;
using WebsocketClientLite.Model;
using WebsocketClientLite.CustomException;
using static WebsocketClientLite.Helper.WebsocketMasking;

namespace WebsocketClientLite.Service;

internal class WebsocketSenderHandler : ISender
{
    private readonly TcpConnectionService _tcpConnectionService;
    private readonly Func<Stream, byte[], CancellationToken, Task> _writeFunc;
    private readonly Action<ConnectionStatus, Exception?> _connectionStatusAction;
    private readonly bool _isExcludingZeroApplicationDataInPong;

    internal WebsocketSenderHandler(
        TcpConnectionService tcpConnectionService,
        Action<ConnectionStatus, Exception?> connectionStatusAction,
        Func<Stream, byte[], CancellationToken, Task> writeFunc,            
        bool isExcludingZeroApplicationDataInPong)
    {
        _tcpConnectionService = tcpConnectionService;
        _connectionStatusAction = connectionStatusAction;
        _writeFunc = writeFunc;
        _isExcludingZeroApplicationDataInPong = isExcludingZeroApplicationDataInPong;
    }

    internal async Task SendConnectHandShake(
        Uri uri,
        CancellationToken ct,
        string? origin = null,            
        IDictionary<string, string>? headers = null,
        IEnumerable<string>? subprotocol = null)
    {
        var handShakeBytes = ClientHandShake.Compose(uri, origin, headers, subprotocol);

        try
        {
            await _writeFunc(_tcpConnectionService.ConnectionStream, handShakeBytes, ct);
        }
        catch (Exception ex)
        {
            _connectionStatusAction(
                ConnectionStatus.Aborted, 
                new WebsocketClientLiteException("Unable to complete handshake", ex.InnerException!));
        }
    }

    public async Task SendText(string message, CancellationToken ct = default) 
       => await ComposeFrameAndSendAsync(
            message,
            OpcodeKind.Text,
            FragmentKind.None,
            ct);

    public async Task SendText(
        IEnumerable<string> messageList,
        CancellationToken ct = default) => 
            await SendList(
                messageList,
                OpcodeKind.Text,
                (text, frag, op) => ComposeFrameAndSendAsync(text, op, frag, ct));


    public async Task SendText(
        string message,
        OpcodeKind opcode,
        FragmentKind fragment,
        CancellationToken ct = default) => 
            await ComposeFrameAndSendAsync(
                    message,
                    opcode,
                    fragment,
                    ct);

    public async Task SendBinary(
        byte[] data,
        CancellationToken ct) => 
            await ComposeFrameAndSendAsync(
                data,
                OpcodeKind.Binary,
                FragmentKind.None,
                ct);

    public async Task SendBinary(
        IEnumerable<byte[]> dataList,
        CancellationToken ct) => 
            await SendList(
                dataList,
                OpcodeKind.Binary,
                (data, frag, op) => ComposeFrameAndSendAsync(data, op, frag, ct));

    public async Task SendBinary(
        byte[] data, 
        OpcodeKind opcode, 
        FragmentKind fragment, CancellationToken ct) =>
            await ComposeFrameAndSendAsync(
                    data,
                    opcode,
                    fragment,
                    ct);

    public async Task SendPing(
        string? message, 
        CancellationToken ct = default) => 
            await ComposeFrameAndSendAsync(
                message,
                OpcodeKind.Ping,
                FragmentKind.None,
                ct);

    public async Task SendPing(
        byte[] data,
        CancellationToken ct = default) =>
            await ComposeFrameAndSendAsync(
                data,
                OpcodeKind.Ping,
                FragmentKind.None,
                ct);

    internal async Task SendPong(
        Dataframe dataframe,
        CancellationToken ct) => 
            await ComposeFrameAndSendAsync(
                dataframe.Binary ?? Array.Empty<byte>(),
                OpcodeKind.Pong,
                FragmentKind.None,
                ct);

    internal async Task SendCloseHandshakeAsync(
        StatusCodes statusCode)
    {
        var closeFrameBodyCode = BitConverter.GetBytes((ushort)statusCode);
        var reason = Encoding.UTF8.GetBytes(statusCode.ToString());

        await ComposeFrameAndSendAsync(
            Combine(closeFrameBodyCode, reason),
            OpcodeKind.Close,
            FragmentKind.None,
            default);

        static byte[] Combine(byte[] a, byte[] b)
        {
            var res = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, res, 0, a.Length);
            Buffer.BlockCopy(b, 0, res, a.Length, b.Length);
            return res;
        }
    }

    private async Task SendList<T>(
        IEnumerable<T> list, 
        OpcodeKind opcode, 
        Func<T, FragmentKind, OpcodeKind, Task> sendTask)
    {
        if (list is null) return;

        var items = list as IList<T> ?? [.. list];
        if (items.Count == 0) return;

        if (items.Count == 1)
        {
            await sendTask(items[0], FragmentKind.None, opcode);
            return;
        }

        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (index == 0)
            {
                await sendTask(item, FragmentKind.First, opcode);
            }
            else if (index == items.Count - 1)
            {
                await sendTask(item, FragmentKind.Last, opcode);
            }
            else
            {
                await sendTask(item, FragmentKind.None, OpcodeKind.Continuation);
            }
        }
    }

    private async Task ComposeFrameAndSendAsync(
        string? message,
        OpcodeKind opcode,
        FragmentKind fragment,
        CancellationToken ct)
    {
        byte[]? bytes = message is not null ? Encoding.UTF8.GetBytes(message) : null;
        await ComposeFrameAndSendAsync(bytes, opcode, fragment, ct);
    }

    private async Task ComposeFrameAndSendAsync(
        byte[]? content, 
        OpcodeKind opcode,
        FragmentKind fragment,
        CancellationToken ct)
    {
        if (!_tcpConnectionService.ConnectionStream.CanWrite)
        {
            throw new WebsocketClientLiteException("Websocket connection stream have been closed");
        }

        // Determine payload length
        int payloadLength = content?.Length ?? 0;
        bool hasPayload = payloadLength > 0;

        // Build header size: 1 (FIN/opcode) + 1/3/9 (payload len) + 4 (mask key)
        int payloadLenField = payloadLength <= 125 ? 1 : (payloadLength <= ushort.MaxValue ? 3 : 9);
        int headerSize = 1 + payloadLenField + 4; // clients must mask

        // Total size: header + encoded payload (same length)
        int totalSize = headerSize + payloadLength;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(totalSize);
        int written = 0;
        try
        {
            // Byte 0: FIN/opcode
            buffer[written++] = DetermineFINBit(opcode, fragment);

            // Payload length and mask bit
            if (payloadLenField == 1)
            {
                buffer[written++] = (byte)(payloadLength | 0x80); // set mask bit
            }
            else if (payloadLenField == 3)
            {
                buffer[written++] = (byte)(126 | 0x80); // 126 + mask
                var len = (ushort)payloadLength;
                buffer[written++] = (byte)(len >> 8);
                buffer[written++] = (byte)(len & 0xFF);
            }
            else
            {
                buffer[written++] = (byte)(127 | 0x80); // 127 + mask
                ulong len = (ulong)payloadLength;
                buffer[written++] = (byte)((len >> 56) & 0xFF);
                buffer[written++] = (byte)((len >> 48) & 0xFF);
                buffer[written++] = (byte)((len >> 40) & 0xFF);
                buffer[written++] = (byte)((len >> 32) & 0xFF);
                buffer[written++] = (byte)((len >> 24) & 0xFF);
                buffer[written++] = (byte)((len >> 16) & 0xFF);
                buffer[written++] = (byte)((len >> 8) & 0xFF);
                buffer[written++] = (byte)(len & 0xFF);
            }

            // Mask key
            var maskKey = CreateMaskKey();
            Buffer.BlockCopy(maskKey, 0, buffer, written, 4);
            written += 4;

            if (hasPayload)
            {
                // Encode in place into buffer after header
                for (int i = 0; i < payloadLength; i++)
                {
                    buffer[written + i] = (byte)(content![i] ^ maskKey[i % 4]);
                }
                written += payloadLength;
            }
            else
            {
                // For zero-length payload, optionally add explicit 0 payload for Pong if interop requires it
                // RFC does not require a 0 byte; mask-only header is acceptable. Keep behavior compatible via flag.
                if (opcode == OpcodeKind.Pong && !_isExcludingZeroApplicationDataInPong)
                {
                    // Explicit 0-length content implies no body; nothing to append.
                    // Old code appended a single 0 byte; with mask header already written, no body is correct.
                }
            }

            await SendFrameAsync(buffer.AsSpan(0, written).ToArray(), opcode, fragment, ct);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        static byte DetermineFINBit(OpcodeKind opcode, FragmentKind fragment)
        {
            if (opcode is OpcodeKind.Continuation)
            {
                return 0;
            }

            return fragment switch
            {
                FragmentKind.None => (byte)((byte)opcode + (byte)FragmentKind.Last),
                FragmentKind.First => (byte)opcode,
                FragmentKind.Last => (byte)FragmentKind.Last,
                _ => throw new NotImplementedException()
            };
        }
    }

    private async Task SendFrameAsync(
        byte[] frame, 
        OpcodeKind opcode,
        FragmentKind fragment,
        CancellationToken ct)
    {
        _connectionStatusAction(
            fragment switch
            {
                FragmentKind.None => opcode is OpcodeKind.Continuation 
                    ? ConnectionStatus.MultiFrameSendingContinue 
                    : ConnectionStatus.SingleFrameSending,
                FragmentKind.First => ConnectionStatus.MultiFrameSendingFirst,
                FragmentKind.Last => ConnectionStatus.MultiFrameSendingLast,
                _ => throw new NotImplementedException(),
            },
            null);

        _connectionStatusAction(
            opcode switch
            {
                OpcodeKind.Continuation => ConnectionStatus.Continuation,
                OpcodeKind.Text => ConnectionStatus.Text,
                OpcodeKind.Binary => ConnectionStatus.Binary,
                OpcodeKind.Close => ConnectionStatus.Close,
                OpcodeKind.Ping => ConnectionStatus.PingSend,
                OpcodeKind.Pong => ConnectionStatus.PongSend,
                _ => throw new NotImplementedException(),
            },
            null);

        try
        {
            await _writeFunc(_tcpConnectionService.ConnectionStream, frame, ct);

            if (opcode is OpcodeKind.Close)
            {
                _connectionStatusAction(ConnectionStatus.Disconnected, null);
            }
            else
            {
                _connectionStatusAction(ConnectionStatus.SendComplete, null);
            }
        }
        catch (Exception ex)
        {
            _connectionStatusAction(
                ConnectionStatus.SendError, 
                new WebsocketClientLiteException("Websocket send error occured.", ex));
        }
    }
}
