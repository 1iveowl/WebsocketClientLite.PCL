using System;
using System.Diagnostics.CodeAnalysis;
using Protocol = WebsocketClientLite.VerifiedProtocol.Protocol;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using IWebsocketClientLite;
using WebsocketClientLite.Helper;
using WebsocketClientLite.Model;
using WebsocketClientLite.CustomException;
using static WebsocketClientLite.Helper.WebsocketMasking;

namespace WebsocketClientLite.Service;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The async-only message semaphore never exposes AvailableWaitHandle; leaving its managed state to GC avoids disposing it under queued senders.")]
internal class WebsocketSenderHandler : ISender
{
    private readonly TcpConnectionService _tcpConnectionService;
    // (stream, buffer, count, ct): writes the first `count` bytes of `buffer`,
    // so a pooled/oversized buffer can be passed without copying to exact size.
    private readonly Func<Stream, byte[], int, CancellationToken, Task> _writeFunc;
    private readonly Action<ConnectionStatus, Exception?> _connectionStatusAction;
    private readonly bool _isExcludingZeroApplicationDataInPong;

    // Data-message ownership spans an entire list; control frames bypass this
    // gate and still serialize at the connection's frame-write gate.
    private readonly SemaphoreSlim _messageGate = new(1, 1);
    private readonly object _stateLock = new();
    private byte _sendState;
    private byte _closeState;
    private readonly TaskCompletionSource<bool> _closeReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Caller holds _stateLock. Only the verified release decision wakes the
    // graceful-close waiter; DisconnectWebsocket also awaits the actual write.
    private bool AdvanceClose(byte action)
    {
        Protocol.CloseStep(_closeState, action, out _closeState, out bool send, out bool release);
        if (release) _closeReady.TrySetResult(true);
        return send;
    }

    internal void NotifyPeerClose()
    {
        lock (_stateLock) AdvanceClose(1);
    }

    internal void NotifyReadFailure()
    {
        lock (_stateLock)
        {
            _sendState = Protocol.SendFailed(_sendState);
            AdvanceClose(2);
        }
    }

    internal async Task WaitForPeerCloseAsync(TimeSpan timeout)
    {
        using var timer = new CancellationTokenSource();
        var delay = Task.Delay(timeout, timer.Token);
        if (await Task.WhenAny(_closeReady.Task, delay).ConfigureAwait(false) == delay)
        {
            lock (_stateLock) AdvanceClose(2);
        }
        else
        {
#if NET8_0_OR_GREATER
            await timer.CancelAsync().ConfigureAwait(false);
#else
            timer.Cancel();
#endif
        }
    }

    internal WebsocketSenderHandler(
        TcpConnectionService tcpConnectionService,
        Action<ConnectionStatus, Exception?> connectionStatusAction,
        Func<Stream, byte[], int, CancellationToken, Task> writeFunc,
        bool isExcludingZeroApplicationDataInPong)
    {
        _tcpConnectionService = tcpConnectionService;
        _connectionStatusAction = connectionStatusAction;
        _writeFunc = writeFunc;
        _isExcludingZeroApplicationDataInPong = isExcludingZeroApplicationDataInPong;
    }

    internal string? ExpectedAccept { get; private set; }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "RFC 6455 requires SHA-1 for the handshake accept value; it is not used for authentication.")]
    [SuppressMessage("Performance", "CA1850:Prefer static HashData method",
        Justification = "The same handshake implementation supports netstandard2.0.")]
    internal async Task SendConnectHandShake(
        Uri uri,
        CancellationToken ct,
        string? origin = null,            
        IDictionary<string, string>? headers = null,
        IEnumerable<string>? subprotocol = null)
    {
        var key = ClientHandShake.GenerateRandomWebSocketKey();
        using (var sha1 = System.Security.Cryptography.SHA1.Create())
            ExpectedAccept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var handShakeBytes = ClientHandShake.Compose(uri, origin, headers, subprotocol, key);

        try
        {
            await _tcpConnectionService.WriteSerializedAsync(
                (stream, token) => _writeFunc(stream, handShakeBytes, handShakeBytes.Length, token), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Propagate: HandshakeHandler turns this into HandshakeSendFailed and
            // the connect path fails with it (no side-channel Abort needed).
            throw new WebsocketClientLiteException("Unable to send websocket handshake.", ex);
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
                text => ProtocolPayload.StrictUtf8.GetByteCount(text ?? string.Empty),
                (text, frag, op) => ComposeFrameAndSendCoreAsync(null, text, op, frag, ct).AsTask(), ct)
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
                data => data?.Length ?? 0,
                (data, frag, op) => ComposeFrameAndSendCoreAsync(data, null, op, frag, ct).AsTask(), ct)
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
        CancellationToken ct)
    {
        var payload = dataframe.Binary ?? Array.Empty<byte>();

        // Slack's RTM API disconnects if a pong with no application data still
        // carries the zero-value payload-length byte. When configured, reply with
        // only the single opcode byte (FIN + Pong opcode) and no length byte.
        // See the "Slack RTM API" section of the README.
        if (_isExcludingZeroApplicationDataInPong && payload.Length == 0)
        {
            _connectionStatusAction(ConnectionStatus.PongSend, null);

            try
            {
                var pongOpcodeOnly = new[] { (byte)((byte)OpcodeKind.Pong + (byte)FragmentKind.Last) };
                await WriteFrameSerializedAsync(pongOpcodeOnly, pongOpcodeOnly.Length, ct).ConfigureAwait(false);
                _connectionStatusAction(ConnectionStatus.SendComplete, null);
            }
            catch (Exception ex)
            {
                var sendError = new WebsocketClientLiteException("Websocket send error occured.", ex);
                _connectionStatusAction(ConnectionStatus.SendError, sendError);
                throw sendError;
            }

            return;
        }

        await ComposeFrameAndSendAsync(
            payload,
            OpcodeKind.Pong,
            FragmentKind.None,
            ct)
        .ConfigureAwait(false);
    }

    internal async Task SendCloseHandshakeAsync(
        StatusCodes statusCode)
    {
        // The close code is a 2-byte unsigned integer in network (big-endian)
        // byte order per RFC 6455 §5.5.1. BitConverter is little-endian on most
        // platforms, so write the bytes explicitly.
        var code = (ushort)statusCode;
        var closeFrameBodyCode = new[] { (byte)(code >> 8), (byte)(code & 0xFF) };
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
        Func<T, int> getLength,
        Func<T, FragmentKind, OpcodeKind, Task> sendTask,
        CancellationToken ct)
    {
        if (list is null) return;
        // Snapshot the sequence and validate every fragment before writing any.
        var items = list.ToArray();
        foreach (var item in items)
        {
            Protocol.Outgoing((byte)opcode, 255, (uint)getLength(item), out bool valid, out _, out _, out _);
            if (!valid) throw new WebsocketClientLiteException("Outgoing fragment exceeds array bounds.");
        }
        if (items.Length == 0) return;
        await _messageGate.WaitAsync(ct).ConfigureAwait(false);
        bool started = false;
        try
        {
            for (int index = 0; index < items.Length; index++)
            {
                ct.ThrowIfCancellationRequested();
                var fragment = items.Length == 1 ? FragmentKind.None : index == 0 ? FragmentKind.First
                    : index == items.Length - 1 ? FragmentKind.Last : FragmentKind.None;
                var op = index == 0 ? opcode : OpcodeKind.Continuation;
                await sendTask(items[index], fragment, op).ConfigureAwait(false);
                started = true;
            }
        }
        catch
        {
            // A failed list must never leave a reusable, incomplete message.
            if (started)
                lock (_stateLock) _sendState = Protocol.SendFailed(_sendState);
            throw;
        }
        finally { _messageGate.Release(); }
    }

    private async Task SendWithMessageGateAsync(byte[]? content, string? text,
        OpcodeKind opcode, FragmentKind fragment, CancellationToken ct)
    {
        bool data = (uint)opcode < 8;
        if (data) await _messageGate.WaitAsync(ct).ConfigureAwait(false);
        try { await ComposeFrameAndSendCoreAsync(content, text, opcode, fragment, ct).ConfigureAwait(false); }
        finally { if (data) _messageGate.Release(); }
    }

    private async Task ComposeFrameAndSendAsync(
        string? message,
        OpcodeKind opcode,
        FragmentKind fragment,
        CancellationToken ct)
    {
        await SendWithMessageGateAsync(null, message, opcode, fragment, ct).ConfigureAwait(false);
    }

    private async Task ComposeFrameAndSendAsync(
        byte[]? content,
        OpcodeKind opcode,
        FragmentKind fragment,
        CancellationToken ct)
    {
        await SendWithMessageGateAsync(content, null, opcode, fragment, ct).ConfigureAwait(false);
    }

    private async ValueTask ComposeFrameAndSendCoreAsync(
        byte[]? content,
        string? text,
        OpcodeKind opcode,
        FragmentKind fragment,
        CancellationToken ct)
    {
        if (!_tcpConnectionService.ConnectionStream.CanWrite)
        {
            throw new WebsocketClientLiteException("Websocket connection stream have been closed");
        }

        int payloadLength = content?.Length ?? (text is not null ? ProtocolPayload.StrictUtf8.GetByteCount(text) : 0);
        if ((uint)opcode > 15 || (uint)fragment > 255)
            throw new WebsocketClientLiteException("Invalid outgoing opcode or fragment kind.");
        Protocol.Outgoing((byte)opcode, (byte)fragment, (uint)payloadLength,
            out bool valid, out byte first, out uint header, out uint total);
        if (!valid) throw new WebsocketClientLiteException("Invalid outgoing frame or frame size exceeds array bounds.");
        int headerSize = (int)header;
        int totalSize = (int)total;
        if (opcode == OpcodeKind.Close)
            ProtocolPayload.ValidateClose(content ?? ProtocolPayload.StrictUtf8.GetBytes(text ?? string.Empty));

        byte[] buffer = ArrayPool<byte>.Shared.Rent(totalSize);
        int written = 0;
        try
        {
            Protocol.WriteHeader(buffer, first, (uint)payloadLength, header);
            written = headerSize - 4;

            int maskOffset = written;
#if NETSTANDARD2_0
            // netstandard2.0 lacks RandomNumberGenerator.Fill(Span<byte>), so fall
            // back to the array-allocating helper and copy the key into the buffer.
            var maskKey = CreateMaskKey();
            Buffer.BlockCopy(maskKey, 0, buffer, maskOffset, 4);
#else
            // Generate the masking key straight into the frame buffer (no extra array).
            System.Security.Cryptography.RandomNumberGenerator.Fill(buffer.AsSpan(maskOffset, 4));
#endif
            written += 4;

            if (payloadLength > 0)
            {
                int payloadOffset = written;
                if (content is not null)
                {
                    if (ReferenceEquals(content, buffer))
                        throw new WebsocketClientLiteException("The payload aliases the rented frame buffer.");
                    Protocol.CopyMasked(content, buffer, (uint)payloadOffset, (uint)payloadLength,
                        buffer[maskOffset], buffer[maskOffset + 1], buffer[maskOffset + 2], buffer[maskOffset + 3]);
                }
                else
                {
                    // Encode the text straight into the frame buffer (no intermediate
                    // array), then mask it in place.
                    ProtocolPayload.StrictUtf8.GetBytes(text!, 0, text!.Length, buffer, payloadOffset);
                    Protocol.MaskSlice(buffer, (uint)payloadOffset, (uint)payloadLength, (uint)maskOffset);
                }
                written += payloadLength;
            }

            await SendFrameCoreAsync(buffer, written, opcode, fragment, ct).ConfigureAwait(false);
        }
        finally
        {
            // Clear before returning: the buffer held the (masked) outgoing
            // payload and mask key. Defense-in-depth so a later renter can't read it.
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

    }

    private Task WriteFrameSerializedAsync(byte[] buffer, int length, CancellationToken ct) =>
        _tcpConnectionService.WriteSerializedAsync(async (stream, token) =>
        {
            byte op = (byte)(buffer[0] & 15);
            lock (_stateLock)
            {
                if (op == 8)
                {
                    if (!AdvanceClose(0)) return;
                }
                // A queued Pong is no longer required after receiving Close.
                // Before the peer replies, Pong is still allowed after our Close.
                else if (op == 10 && (_closeState & 6) != 0) return;
                else if (op != 10 && _closeState != 0)
                    throw new WebsocketClientLiteException("The closing handshake has started.");
                Protocol.SendStep(_sendState, op, (buffer[0] & 128) != 0, out bool ok, out byte next);
                if (!ok) throw new WebsocketClientLiteException("Invalid outgoing message sequence or sender is closed/failed.");
                _sendState = next;
            }
            try { await _writeFunc(stream, buffer, length, token).ConfigureAwait(false); }
            catch
            {
                // A write may have transferred only part of a frame: no further
                // frame (even Close) may be appended to an unknown wire prefix.
                NotifyReadFailure();
                throw;
            }
        }, ct);

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
            await WriteFrameSerializedAsync(buffer, length, ct).ConfigureAwait(false);

            // Disconnected belongs to the connection's completed closing handshake.
            if (opcode is not OpcodeKind.Close)
                _connectionStatusAction(ConnectionStatus.SendComplete, null);
        }
        catch (Exception ex)
        {
            // Report through the status channel AND throw: callers awaiting a
            // Send* method must observe the failure rather than a silent success.
            var sendError = new WebsocketClientLiteException("Websocket send error occured.", ex);
            _connectionStatusAction(ConnectionStatus.SendError, sendError);
            throw sendError;
        }
    }
}
