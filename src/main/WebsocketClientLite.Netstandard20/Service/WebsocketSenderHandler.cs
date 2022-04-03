using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        internal WebsocketSenderHandler(
            IObserver<ConnectionStatus> observerConnectionStatus,
            Stream tcpStream)
        {
            _observerConnectionStatus = observerConnectionStatus;
            _tcpStream = tcpStream;
        }

        internal async Task SendConnectHandShakeAsync(
            Uri uri,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocol = null,
            bool isSocketIOv4 = false
)
        {
            var handShake = ClientHandShake.Compose(uri, origin, headers, subprotocol, isSocketIOv4);

            try
            {
                await _tcpStream.WriteAsync(handShake, 0, handShake.Length);
                await _tcpStream.FlushAsync();
            }
            catch (Exception ex)
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.Aborted);
                throw new WebsocketClientLiteException("Unable to complete handshake", ex.InnerException);
            }
        }

        public async Task SendTextAsync(string message)
        {
            var msgAsBytes = Encoding.UTF8.GetBytes(message);

            await ComposeFrameAndSendAsync(msgAsBytes, FrameType.Single);
        }

        public async Task SendTextAsync(string[] messageList)
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
                    await ComposeFrameAndSendAsync(Encoding.UTF8.GetBytes(messageList[i]), FrameType.Continuation);
                }

                await ComposeFrameAndSendAsync(Encoding.UTF8.GetBytes(messageList.Last()), FrameType.LastInMultipleFrames);
            }
            finally
            {
                _isSendingMultipleFrames = false;
            }

            _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingBegin);

            for (var i = 1; i < messageList.Length - 1; i++)
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingContinue);
                await ComposeFrameAndSendAsync(Encoding.UTF8.GetBytes(messageList[i]), FrameType.Continuation);
            }
            await ComposeFrameAndSendAsync(Encoding.UTF8.GetBytes(messageList.Last()), FrameType.LastInMultipleFrames);            
        }

        
        public async Task SendTextAsync(string message, FrameType frameType)
        {
            if (_isSendingMultipleFrames)
            {
                if (frameType == FrameType.FirstOfMultipleFrames || frameType == FrameType.Single)
                {
                    await ComposeFrameAndSendAsync(Encoding.UTF8.GetBytes("_sequence aborted error_"), FrameType.LastInMultipleFrames);
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
                    _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingSingle);
                    await ComposeFrameAndSendAsync(Encoding.UTF8.GetBytes(message), FrameType.Single);
                    break;
                case FrameType.FirstOfMultipleFrames:
                    _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingBegin);
                    _isSendingMultipleFrames = true;
                    await ComposeFrameAndSendAsync(Encoding.UTF8.GetBytes(message), FrameType.FirstOfMultipleFrames);
                    break;
                case FrameType.LastInMultipleFrames:
                    _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingLast);
                    await ComposeFrameAndSendAsync(Encoding.UTF8.GetBytes(message), FrameType.LastInMultipleFrames);
                    _isSendingMultipleFrames = false;
                    break;
                case FrameType.Continuation:
                    _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingContinue);
                    await ComposeFrameAndSendAsync(Encoding.UTF8.GetBytes(message), FrameType.Continuation);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(frameType), frameType, null);
            }
        }

        internal async Task SendCloseHandshakeAsync(StatusCodes statusCode)
        {
            var closeFrameBodyCode = BitConverter.GetBytes((ushort)statusCode);
            var reason = Encoding.UTF8.GetBytes(statusCode.ToString());

            await ComposeFrameAndSendAsync(closeFrameBodyCode.Concat(reason).ToArray(),
                FrameType.CloseControlFrame);
        }

        private async Task ComposeFrameAndSendAsync(byte[] content, FrameType frameType)
        {
            var firstByte = new byte[1];

            firstByte[0] = frameType switch
            {
                FrameType.Single => 129,
                FrameType.FirstOfMultipleFrames => 1,
                FrameType.Continuation => 0,
                FrameType.LastInMultipleFrames => 128,
                FrameType.CloseControlFrame => 136,
                _ => throw new ArgumentOutOfRangeException(nameof(frameType), frameType, null),
            };

            var payloadBytes = CreatePayloadBytes(content.Length, isMasking: true);
            var maskKey = CreateMaskKey();
            var maskedMessage = Encode(content, maskKey);
            var frame = firstByte.Concat(payloadBytes).Concat(maskKey).Concat(maskedMessage).ToArray();

            await SendFrameAsync(frame);
        }

        private async Task SendFrameAsync(byte[] frame)
        {
            if (!_tcpStream.CanWrite)
            {
                throw new WebsocketClientLiteException("Websocket connection have been closed");
            }

            try
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.Sending);
                await _tcpStream.WriteAsync(frame, 0, frame.Length);
                await _tcpStream.FlushAsync();
                _observerConnectionStatus.OnNext(ConnectionStatus.SendComplete);
            }
            catch (Exception ex)
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.SendError);
                _observerConnectionStatus.OnError(ex);
            }
        }
    }
}
