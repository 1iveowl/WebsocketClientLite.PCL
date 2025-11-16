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
            await _writeFunc(_tcpConnectionService.ConnectionStream, handShakeBytes, ct).ConfigureAwait(false);
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
            ct)
        .ConfigureAwait(false);

    public async Task SendText(
        IEnumerable<string> messageList,
        CancellationToken ct = default) => 
            await SendList(
                messageList,
                OpcodeKind.Text,
                (text, frag, op) => ComposeFrameAndSendAsync(text, op, frag, ct))
            .ConfigureAwait(false);


    public async Task SendText(
        string message,
        OpcodeKind opcode,
        FragmentKind fragment,
        CancellationToken ct = default) => 
            await ComposeFrameAndSendAsync(
                    message,
                    opcode,
                    fragment,
                    ct)
                .ConfigureAwait(false);

    public async Task SendBinary(
        byte[] data,
        CancellationToken ct) => 
            await ComposeFrameAndSendAsync(
                data,
                OpcodeKind.Binary,
                FragmentKind.None,
                ct)
            .ConfigureAwait(false);

    public async Task SendBinary(
        IEnumerable<byte[]> dataList,
        CancellationToken ct) => 
            await SendList(
                dataList,
                OpcodeKind.Binary,
                (data, frag, op) => ComposeFrameAndSendAsync(data, op, frag, ct))
            .ConfigureAwait(false);

    public async Task SendBinary(
        byte[] data, 
        OpcodeKind opcode, 
        FragmentKind fragment, CancellationToken ct) =>
            await ComposeFrameAndSendAsync(
                    data,
                    opcode,
                    fragment,
                    ct)
            .ConfigureAwait(false);

    public async Task SendPing(
        string? message, 
        CancellationToken ct = default) => 
            await ComposeFrameAndSendAsync(
                message,
                OpcodeKind.Ping,
                FragmentKind.None,
                ct)
            .ConfigureAwait(false);

    public async Task SendPing(
        byte[] data,
        CancellationToken ct = default) =>
            await ComposeFrameAndSendAsync(
                data,
                OpcodeKind.Ping,
                FragmentKind.None,
                ct)
            .ConfigureAwait(false);

    internal async Task SendPong(
        Dataframe dataframe,
        CancellationToken ct) => 
            await ComposeFrameAndSendAsync(
                dataframe.Binary ?? Array.Empty<byte>(),
                OpcodeKind.Pong,
                FragmentKind.None,
                ct)
            .ConfigureAwait(false);

    internal async Task SendCloseHandshakeAsync(
        StatusCodes statusCode)
    {
        var closeFrameBodyCode = BitConverter.GetBytes((ushort)statusCode);
        var reason = Encoding.UTF8.GetBytes(statusCode.ToString());

        await ComposeFrameAndSendAsync(
            Combine(closeFrameBodyCode, reason),
            OpcodeKind.Close,
            FragmentKind.None,
            default)
            .ConfigureAwait(false);

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
            await sendTask(items[0], FragmentKind.None, opcode).ConfigureAwait(false);
            return;
        }

        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (index == 0)
            {
                await sendTask(item, FragmentKind.First, opcode).ConfigureAwait(false);
            }
            else if (index == items.Count - 1)
            {
                await sendTask(item, FragmentKind.Last, opcode).ConfigureAwait(false) ;
            }
            else
            {
                await sendTask(item, FragmentKind.None, OpcodeKind.Continuation).ConfigureAwait(false);
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
        await ComposeFrameAndSendCoreAsync(bytes, opcode, fragment, ct).ConfigureAwait(false);
    }

    private async Task ComposeFrameAndSendAsync(
        byte[]? content, 
        OpcodeKind opcode,
        FragmentKind fragment,
        CancellationToken ct)
    {
        await ComposeFrameAndSendCoreAsync(content, opcode, fragment, ct).ConfigureAwait(false);
    }

    private async ValueTask ComposeFrameAndSendCoreAsync(
        byte[]? content,
        OpcodeKind opcode,
        FragmentKind fragment,
        CancellationToken ct)
    {
        if (!_tcpConnectionService.ConnectionStream.CanWrite)
        {
            throw new WebsocketClientLiteException("Websocket connection stream have been closed");
        }

        int payloadLength = content?.Length ?? 0;
        int payloadLenField = payloadLength <= 125 ? 1 : (payloadLength <= ushort.MaxValue ? 3 : 9);
        int headerSize = 1 + payloadLenField + 4;
        int totalSize = headerSize + payloadLength;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(totalSize);
        int written = 0;
        try
        {
            buffer[written++] = DetermineFINBit(opcode, fragment);

            if (payloadLenField == 1)
            {
                buffer[written++] = (byte)(payloadLength | 0x80);
            }
            else if (payloadLenField == 3)
            {
                buffer[written++] = (byte)(126 | 0x80);
                var len = (ushort)payloadLength;
                buffer[written++] = (byte)(len >> 8);
                buffer[written++] = (byte)(len & 0xFF);
            }
            else
            {
                buffer[written++] = (byte)(127 | 0x80);
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

            var maskKey = CreateMaskKey();
            Buffer.BlockCopy(maskKey, 0, buffer, written, 4);
            written += 4;

            if (payloadLength > 0)
            {
                for (int i = 0; i < payloadLength; i++)
                {
                    buffer[written + i] = (byte)(content![i] ^ maskKey[i % 4]);
                }
                written += payloadLength;
            }

            await SendFrameCoreAsync(buffer, written, opcode, fragment, ct).ConfigureAwait(false);
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

    private async ValueTask SendFrameCoreAsync(
        byte[] buffer,
        int length,
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
            var frame = new byte[length];
            Buffer.BlockCopy(buffer, 0, frame, 0, length);
            await _writeFunc(_tcpConnectionService.ConnectionStream, frame, ct).ConfigureAwait(false);

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
