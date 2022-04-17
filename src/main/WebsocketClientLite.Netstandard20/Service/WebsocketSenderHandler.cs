using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Extension;
using WebsocketClientLite.PCL.CustomException;
using static WebsocketClientLite.PCL.Helper.WebsocketMasking;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketSenderHandler : ISender
    {
        private readonly TcpConnectionService _tcpConnectionService;
        private readonly Func<Stream, byte[], CancellationToken, Task> _writeFunc;
        private readonly Action<ConnectionStatus, Exception> _connectionStatusAction;
        private readonly bool _isExcludingZeroApplicationDataInPong;

        internal WebsocketSenderHandler(
            TcpConnectionService tcpConnectionService,
            Action<ConnectionStatus, Exception> connectionStatusAction,
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
            string origin = null,            
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocol = null)
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
                    new WebsocketClientLiteException("Unable to complete handshake", ex.InnerException));
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
            string message, 
            CancellationToken ct = default) => 
                await ComposeFrameAndSendAsync(
                    message,
                    OpcodeKind.Ping,
                    FragmentKind.None,
                    ct);

        internal async Task SendPong(
            Dataframe dataframe,
            CancellationToken ct) => 
                await ComposeFrameAndSendAsync(
                    dataframe.Binary,
                    OpcodeKind.Pong,
                    FragmentKind.None,
                    ct);

        internal async Task SendCloseHandshakeAsync(
            StatusCodes statusCode)
        {
            var closeFrameBodyCode = BitConverter.GetBytes((ushort)statusCode);
            var reason = Encoding.UTF8.GetBytes(statusCode.ToString());

            await ComposeFrameAndSendAsync(
                closeFrameBodyCode.Concat(reason).ToArray(),
                OpcodeKind.Close,
                FragmentKind.None,
                default);
        }

        private async Task SendList<T>(
            IEnumerable<T> list, 
            OpcodeKind opcode, 
            Func<T, FragmentKind, OpcodeKind, Task> sendTask)
        {
            if (!list?.Any() ?? true) return;

            if (list.Count() == 1)
            {
                await sendTask(list.FirstOrDefault(), FragmentKind.None, opcode);
                return;
            }

            await foreach (var (item, index) in list.Select((item, index) => (item, index)).ToAsyncEnumerable())
            {
                if (IsFirstPosition(index))
                {
                    await sendTask(item, FragmentKind.First, opcode);
                }
                else if (IsLastPosition(index))
                {
                    await sendTask(item, FragmentKind.Last, opcode);
                }
                else
                {
                    await sendTask(item, FragmentKind.None, OpcodeKind.Continuation);
                }
            }

            bool IsLastPosition(int i) => i == list.Count() - 1;

            bool IsFirstPosition(int i) => i == 0;
        }

        private async Task ComposeFrameAndSendAsync(
            string message,
            OpcodeKind opcode,
            FragmentKind fragment,
            CancellationToken ct) => 
                await ComposeFrameAndSendAsync(
                    message is not null ? Encoding.UTF8.GetBytes(message) : default,
                    opcode,
                    fragment,
                    ct);

        private async Task ComposeFrameAndSendAsync(
            byte[] content, 
            OpcodeKind opcode,
            FragmentKind fragment,
            CancellationToken ct)
        {
            var frame = new byte[1] { DetermineFINBit(opcode, fragment) };

            if (content is not null)
            {
                var maskKey = CreateMaskKey();
                frame = frame.Concat(CreatePayloadBytes(content.Length, isMasking: true))
                    .Concat(maskKey)
                    .Concat(Encode(content, maskKey))
                    .ToArray();
            }
            else if (!_isExcludingZeroApplicationDataInPong)
            {
                frame = frame
                    .Concat(new byte[1] { 0 })
                    .ToArray();                
            }

            await SendFrameAsync(
                frame, 
                opcode,
                fragment,
                ct);

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
            if (!_tcpConnectionService.ConnectionStream.CanWrite)
            {
                throw new WebsocketClientLiteException("Websocket connection stream have been closed");
            }

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

            if (ct == default)
            {
                var cts = new CancellationTokenSource();
                ct = cts.Token;
            }

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
}
