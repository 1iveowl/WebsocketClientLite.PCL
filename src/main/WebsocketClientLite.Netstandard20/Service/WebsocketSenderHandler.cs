using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;
using static WebsocketClientLite.PCL.Helper.WebsocketMasking;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketSenderHandler : ISender
    {
        private readonly TcpConnectionService _tcpConnectionService;
        private readonly Func<Stream, byte[], CancellationToken, Task> _writeFunc;
        private readonly Action<ConnectionStatus, Exception> _connectionStatusAction;

        private bool _isSendingMultipleFrames;

        internal WebsocketSenderHandler(
            TcpConnectionService tcpConnectionService,
            Action<ConnectionStatus, Exception> connectionStatusAction,
            Func<Stream, byte[], CancellationToken, Task> writeFunc)
        {
            _tcpConnectionService = tcpConnectionService;
            _connectionStatusAction = connectionStatusAction;
            _writeFunc = writeFunc;
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

        public async Task SendTextAsync(string message, CancellationToken ct = default) 
           => await ComposeFrameAndSendAsync(
                message,
                OpcodeKind.Text,
                FragmentKind.None,
                ct);

        public async Task SendTextAsync(
            string[] messageList, 
            CancellationToken ct = default)
        {
            if (!messageList?.Any() ?? true) return;

            if (messageList.Length == 1)
            {
                await ComposeFrameAndSendAsync(
                        messageList[0],
                        OpcodeKind.Text,
                        FragmentKind.None,
                        ct);
                return;
            }

            try
            {
                await ComposeFrameAndSendAsync(
                        messageList[0],
                        OpcodeKind.Text,
                        FragmentKind.First,
                        ct);

                for (var i = 1; i < messageList.Length - 1; i++)
                {
                    await ComposeFrameAndSendAsync(
                        messageList[i], 
                        OpcodeKind.Continuation,
                        FragmentKind.None,
                        ct);
                }

                await ComposeFrameAndSendAsync(
                    messageList.Last(),
                    OpcodeKind.Text,
                    FragmentKind.Last, 
                    ct);
            }
            finally
            {
                _isSendingMultipleFrames = false;
            }   
        }

        
        public async Task SendTextAsync(
            string message,
            OpcodeKind opcode,
            FragmentKind fragment,
            CancellationToken ct = default)
        {
            await ComposeFrameAndSendAsync(
                        message,
                        opcode,
                        fragment,
                        ct);

            //if (_isSendingMultipleFrames)
            //{
            //    if (frameType == OpcodeKind.FirstOfMultipleFrames || frameType == OpcodeKind.Text)
            //    {
            //        await ComposeFrameAndSendAsync(Encoding.UTF8.GetBytes("_sequence aborted error_"), OpcodeKind.LastInMultipleFrames, ct);
            //        _isSendingMultipleFrames = false;

            //        throw new WebsocketClientLiteException("Multiple frames is progress. Frame must be a Continuation Frame or Last Frams in sequence. Multiple frame sequence aborted and finalized");
            //    }
            //}

            //if (!_isSendingMultipleFrames && frameType != OpcodeKind.FirstOfMultipleFrames)
            //{
            //    if (frameType == OpcodeKind.Continuation || frameType == OpcodeKind.LastInMultipleFrames)
            //    {
            //        throw new WebsocketClientLiteException("Multiple frames sequence is not in initiated. Frame cannot be of a Continuation Frame or a Last Frame type");
            //    }
            //}

            //switch (frameType)
            //{
            //    case OpcodeKind.Text:
            //        await ComposeFrameAndSendAsync(
            //            message, 
            //            OpcodeKind.Text, 
            //            ct);
            //        break;
            //    case OpcodeKind.FirstOfMultipleFrames:
            //        _isSendingMultipleFrames = true;
            //        await ComposeFrameAndSendAsync(
            //            message, 
            //            OpcodeKind.FirstOfMultipleFrames, 
            //            ct);
            //        break;
            //    case OpcodeKind.LastInMultipleFrames:
            //        await ComposeFrameAndSendAsync(
            //            message, 
            //            OpcodeKind.LastInMultipleFrames, 
            //            ct);
            //        _isSendingMultipleFrames = false;
            //        break;
            //    case OpcodeKind.Continuation:
            //        await ComposeFrameAndSendAsync(
            //            message, 
            //            OpcodeKind.Continuation, 
            //            ct);
            //        break;
            //    default:
            //        throw new ArgumentOutOfRangeException(nameof(frameType), frameType, null);
            //}
        }

        public Task SendPingWithText(string message, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task SendPing(string message, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task SendPong(string message, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        internal async Task SendCloseHandshakeAsync(
            StatusCodes statusCode, 
            CancellationToken ct = default)
        {
            var closeFrameBodyCode = BitConverter.GetBytes((ushort)statusCode);
            var reason = Encoding.UTF8.GetBytes(statusCode.ToString());

            await ComposeFrameAndSendAsync(
                closeFrameBodyCode.Concat(reason).ToArray(),
                OpcodeKind.Close,
                FragmentKind.None,
                ct);
        }

        private async Task ComposeFrameAndSendAsync(
            string message,
            OpcodeKind opcode,
            FragmentKind fragment,
            CancellationToken ct) => 
                await ComposeFrameAndSendAsync(
                    Encoding.UTF8.GetBytes(message),
                    opcode,
                    fragment,
                    ct);

        private async Task ComposeFrameAndSendAsync(
            byte[] content, 
            OpcodeKind opcode,
            FragmentKind fragment,
            CancellationToken ct)
        {
            var maskKey = CreateMaskKey();

            var frame = new byte[1] { DetermineFINBit(opcode, fragment) }
                .Concat(CreatePayloadBytes(content.Length, isMasking: true))
                .Concat(maskKey)
                .Concat(Encode(content, maskKey))
                .ToArray();

            await SendFrameAsync(
                frame, 
                opcode,
                fragment,
                ct);

            static byte DetermineFINBit(OpcodeKind opcode, FragmentKind fragment)
            {
                if (opcode == OpcodeKind.Continuation)
                {
                    return 0;
                }

                return fragment switch
                {
                    FragmentKind.None => (byte)(opcode + (byte)FragmentKind.Last),
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
                opcode switch
                {
                    OpcodeKind.Continuation => ConnectionStatus.Continuation,
                    OpcodeKind.Text => ConnectionStatus.Text,
                    OpcodeKind.Binary => ConnectionStatus.Binary,
                    OpcodeKind.Close => ConnectionStatus.Close,
                    OpcodeKind.Ping => ConnectionStatus.Ping,
                    OpcodeKind.Pong => ConnectionStatus.Pong,
                    _ => throw new NotImplementedException(),
                }, 
                null);


            _connectionStatusAction(
                fragment switch
                {
                    FragmentKind.None => opcode == OpcodeKind.Continuation 
                        ? ConnectionStatus.MultiFrameSendingContinue 
                        : ConnectionStatus.SingleFrameSending,
                    FragmentKind.First => ConnectionStatus.MultiFrameSendingFirst,
                    FragmentKind.Last => ConnectionStatus.MultiFrameSendingLast,
                    _ => throw new NotImplementedException(),
                },
                null);

            //switch (opcode)
            //{
            //    case OpcodeKind.Text:
            //        _connectionStatusAction(ConnectionStatus.Text, null);
            //        break;
            //    //case OpcodeKind.FirstOfMultipleFrames:
            //    //    _connectionStatusAction(ConnectionStatus.MultiFrameSendingBegin, null);
            //    //    //_isSendingMultipleFrames = true;
            //    //    break;
            //    //case OpcodeKind.LastInMultipleFrames:
            //    //    _connectionStatusAction(ConnectionStatus.MultiFrameSendingLast, null);
            //    //    //_isSendingMultipleFrames = false;
            //    //    break;
            //    case OpcodeKind.Continuation:
            //        _connectionStatusAction(ConnectionStatus.Continuation, null);
            //       break;
            //    case OpcodeKind.Close:
            //        _connectionStatusAction(ConnectionStatus.Close, null);
            //        break;
            //    default:
            //        throw new ArgumentOutOfRangeException(nameof(opcode), opcode, null);
            //}

            

            if (ct == default)
            {
                var cts = new CancellationTokenSource();
                ct = cts.Token;
            }

            try
            {
                await _writeFunc(_tcpConnectionService.ConnectionStream, frame, ct);

                if (opcode == OpcodeKind.Close)
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
