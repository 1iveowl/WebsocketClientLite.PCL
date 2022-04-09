using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;
using static WebsocketClientLite.PCL.Helper.WebsocketMasking;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketSenderHandler : ISender
    {
        private bool _isSendingMultipleFrames;
        private readonly Stream _tcpStream;
        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;
        private readonly Func<Stream, byte[], CancellationToken, Task> _writeFunc;

        internal WebsocketSenderHandler(
            IObserver<ConnectionStatus> observerConnectionStatus,
            Stream tcpStream,
            Func<Stream, byte[], CancellationToken, Task> writeFunc)
        {
            _observerConnectionStatus = observerConnectionStatus;
            _tcpStream = tcpStream;
            _writeFunc = writeFunc;
        }

        internal async Task SendConnectHandShake(
            Uri uri,
            CancellationToken ct,
            string origin = null,            
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocol = null,
            bool isSocketIOv4 = false
)
        {
            var handShakeBytes = ClientHandShake
                .Compose(uri, origin, headers, subprotocol, isSocketIOv4);

            try
            {
                await _writeFunc(_tcpStream, handShakeBytes, ct);
            }
            catch (Exception ex)
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.Aborted);
                throw new WebsocketClientLiteException("Unable to complete handshake", ex.InnerException);
            }
        }

        public async Task SendTextAsync(string message, CancellationToken ct = default) 
           => await ComposeFrameAndSendAsync(
                message,
                FrameType.Single,
                ct);

        public async Task SendTextAsync(
            string[] messageList, 
            CancellationToken ct = default)
        {
            if (!messageList?.Any() ?? true) return;

            if (messageList.Length == 1)
            {
                await SendTextAsync(messageList[0], FrameType.Single);
                return;
            }

            try
            {
                await SendTextAsync(messageList[0], FrameType.FirstOfMultipleFrames);

                for (var i = 1; i < messageList.Length - 1; i++)
                {
                    await ComposeFrameAndSendAsync(
                        messageList[i], 
                        FrameType.Continuation, 
                        ct);
                }

                await ComposeFrameAndSendAsync(
                    messageList.Last(), 
                    FrameType.LastInMultipleFrames, 
                    ct);
            }
            finally
            {
                _isSendingMultipleFrames = false;
            }   
        }

        
        public async Task SendTextAsync(
            string message, 
            FrameType frameType, 
            CancellationToken ct = default)
        {
            if (_isSendingMultipleFrames)
            {
                if (frameType == FrameType.FirstOfMultipleFrames || frameType == FrameType.Single)
                {
                    await ComposeFrameAndSendAsync(Encoding.UTF8.GetBytes("_sequence aborted error_"), FrameType.LastInMultipleFrames, ct);
                    _isSendingMultipleFrames = false;

                    throw new WebsocketClientLiteException("Multiple frames is progress. Frame must be a Continuation Frame or Last Frams in sequence. Multiple frame sequence aborted and finalized");
                }
            }

            if (!_isSendingMultipleFrames && frameType != FrameType.FirstOfMultipleFrames)
            {
                if (frameType == FrameType.Continuation || frameType == FrameType.LastInMultipleFrames)
                {
                    throw new WebsocketClientLiteException("Multiple frames sequence is not in initiated. Frame cannot be of a Continuation Frame or a Last Frame type");
                }
            }

            switch (frameType)
            {
                case FrameType.Single:
                    await ComposeFrameAndSendAsync(
                        message, 
                        FrameType.Single, 
                        ct);
                    break;
                case FrameType.FirstOfMultipleFrames:
                    _isSendingMultipleFrames = true;
                    await ComposeFrameAndSendAsync(
                        message, 
                        FrameType.FirstOfMultipleFrames, 
                        ct);
                    break;
                case FrameType.LastInMultipleFrames:
                    await ComposeFrameAndSendAsync(
                        message, 
                        FrameType.LastInMultipleFrames, 
                        ct);
                    _isSendingMultipleFrames = false;
                    break;
                case FrameType.Continuation:
                    await ComposeFrameAndSendAsync(
                        message, 
                        FrameType.Continuation, 
                        ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(frameType), frameType, null);
            }
        }

        internal async Task SendCloseHandshakeAsync(
            StatusCodes statusCode, 
            CancellationToken ct)
        {
            var closeFrameBodyCode = BitConverter.GetBytes((ushort)statusCode);
            var reason = Encoding.UTF8.GetBytes(statusCode.ToString());

            await ComposeFrameAndSendAsync(
                closeFrameBodyCode.Concat(reason).ToArray(),
                FrameType.CloseControlFrame,
                ct);
        }

        private async Task ComposeFrameAndSendAsync(
            string message,
            FrameType frameType,
            CancellationToken ct) => 
                await ComposeFrameAndSendAsync(
                    Encoding.UTF8.GetBytes(message),
                    frameType,
                    ct);

        private async Task ComposeFrameAndSendAsync(
            byte[] content, 
            FrameType frameType,
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
            FrameType frameType, 
            CancellationToken ct)
        {
            if (!_tcpStream.CanWrite)
            {
                throw new WebsocketClientLiteException("Websocket connection have been closed");
            }

            switch (frameType)
            {
                case FrameType.Single:
                    _observerConnectionStatus.OnNext(ConnectionStatus.Sending);
                    break;
                case FrameType.FirstOfMultipleFrames:
                    _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingBegin);
                    _isSendingMultipleFrames = true;
                    break;
                case FrameType.LastInMultipleFrames:
                    _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingLast);
                    _isSendingMultipleFrames = false;
                    break;
                case FrameType.Continuation:
                    _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingContinue);
                   break;
                case FrameType.CloseControlFrame:
                    _observerConnectionStatus.OnNext(ConnectionStatus.Disconnecting);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(frameType), frameType, null);
                    //_observerConnectionStatus.OnNext(ConnectionStatus.Sending);
                    //break;
                //throw new ArgumentOutOfRangeException(nameof(frameType), frameType, null);
            }

            try
            {
                if (ct == default)
                {
                    var cts = new CancellationTokenSource();
                    ct = cts.Token;
                }

                await _writeFunc(_tcpStream, frame, ct);

                if (frameType == FrameType.CloseControlFrame)
                {
                    _observerConnectionStatus.OnNext(ConnectionStatus.Disconnected);
                }
                else
                {
                    _observerConnectionStatus.OnNext(ConnectionStatus.SendComplete);
                }                
            }
            catch (Exception ex)
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.SendError);
                _observerConnectionStatus.OnError(ex);
            }
        }
    }
}
