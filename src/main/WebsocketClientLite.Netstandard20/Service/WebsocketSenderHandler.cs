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
                FrameTypeKind.Single,
                ct);

        public async Task SendTextAsync(
            string[] messageList, 
            CancellationToken ct = default)
        {
            if (!messageList?.Any() ?? true) return;

            if (messageList.Length == 1)
            {
                await SendTextAsync(messageList[0], FrameTypeKind.Single);
                return;
            }

            try
            {
                await SendTextAsync(messageList[0], FrameTypeKind.FirstOfMultipleFrames);

                for (var i = 1; i < messageList.Length - 1; i++)
                {
                    await ComposeFrameAndSendAsync(
                        messageList[i], 
                        FrameTypeKind.Continuation, 
                        ct);
                }

                await ComposeFrameAndSendAsync(
                    messageList.Last(), 
                    FrameTypeKind.LastInMultipleFrames, 
                    ct);
            }
            finally
            {
                _isSendingMultipleFrames = false;
            }   
        }

        
        public async Task SendTextAsync(
            string message, 
            FrameTypeKind frameType, 
            CancellationToken ct = default)
        {
            if (_isSendingMultipleFrames)
            {
                if (frameType == FrameTypeKind.FirstOfMultipleFrames || frameType == FrameTypeKind.Single)
                {
                    await ComposeFrameAndSendAsync(Encoding.UTF8.GetBytes("_sequence aborted error_"), FrameTypeKind.LastInMultipleFrames, ct);
                    _isSendingMultipleFrames = false;

                    throw new WebsocketClientLiteException("Multiple frames is progress. Frame must be a Continuation Frame or Last Frams in sequence. Multiple frame sequence aborted and finalized");
                }
            }

            if (!_isSendingMultipleFrames && frameType != FrameTypeKind.FirstOfMultipleFrames)
            {
                if (frameType == FrameTypeKind.Continuation || frameType == FrameTypeKind.LastInMultipleFrames)
                {
                    throw new WebsocketClientLiteException("Multiple frames sequence is not in initiated. Frame cannot be of a Continuation Frame or a Last Frame type");
                }
            }

            switch (frameType)
            {
                case FrameTypeKind.Single:
                    await ComposeFrameAndSendAsync(
                        message, 
                        FrameTypeKind.Single, 
                        ct);
                    break;
                case FrameTypeKind.FirstOfMultipleFrames:
                    _isSendingMultipleFrames = true;
                    await ComposeFrameAndSendAsync(
                        message, 
                        FrameTypeKind.FirstOfMultipleFrames, 
                        ct);
                    break;
                case FrameTypeKind.LastInMultipleFrames:
                    await ComposeFrameAndSendAsync(
                        message, 
                        FrameTypeKind.LastInMultipleFrames, 
                        ct);
                    _isSendingMultipleFrames = false;
                    break;
                case FrameTypeKind.Continuation:
                    await ComposeFrameAndSendAsync(
                        message, 
                        FrameTypeKind.Continuation, 
                        ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(frameType), frameType, null);
            }
        }

        internal async Task SendCloseHandshakeAsync(
            StatusCodes statusCode, 
            CancellationToken ct = default)
        {
            var closeFrameBodyCode = BitConverter.GetBytes((ushort)statusCode);
            var reason = Encoding.UTF8.GetBytes(statusCode.ToString());

            await ComposeFrameAndSendAsync(
                closeFrameBodyCode.Concat(reason).ToArray(),
                FrameTypeKind.Close,
                ct);
        }

        private async Task ComposeFrameAndSendAsync(
            string message,
            FrameTypeKind frameType,
            CancellationToken ct) => 
                await ComposeFrameAndSendAsync(
                    Encoding.UTF8.GetBytes(message),
                    frameType,
                    ct);

        private async Task ComposeFrameAndSendAsync(
            byte[] content, 
            FrameTypeKind frameType,
            CancellationToken ct)
        {
            var maskKey = CreateMaskKey();

            var frame = new byte[1] { (byte)frameType }
                .Concat(CreatePayloadBytes(content.Length, isMasking: true))
                .Concat(maskKey)
                .Concat(Encode(content, maskKey))
                .ToArray();

            await SendFrameAsync(
                frame, 
                frameType, 
                ct);
        }

        private async Task SendFrameAsync(
            byte[] frame, 
            FrameTypeKind frameType, 
            CancellationToken ct)
        {
            if (!_tcpConnectionService.ConnectionStream.CanWrite)
            {
                throw new WebsocketClientLiteException("Websocket connection stream have been closed");
            }

            switch (frameType)
            {
                case FrameTypeKind.Single:
                    _connectionStatusAction(ConnectionStatus.Sending, null);
                    break;
                case FrameTypeKind.FirstOfMultipleFrames:
                    _connectionStatusAction(ConnectionStatus.MultiFrameSendingBegin, null);
                    _isSendingMultipleFrames = true;
                    break;
                case FrameTypeKind.LastInMultipleFrames:
                    _connectionStatusAction(ConnectionStatus.MultiFrameSendingLast, null);
                    _isSendingMultipleFrames = false;
                    break;
                case FrameTypeKind.Continuation:
                    _connectionStatusAction(ConnectionStatus.MultiFrameSendingContinue, null);
                   break;
                case FrameTypeKind.Close:
                    _connectionStatusAction(ConnectionStatus.Disconnecting, null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(frameType), frameType, null);
            }

            if (ct == default)
            {
                var cts = new CancellationTokenSource();
                ct = cts.Token;
            }

            try
            {
                await _writeFunc(_tcpConnectionService.ConnectionStream, frame, ct);

                if (frameType == FrameTypeKind.Close)
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
