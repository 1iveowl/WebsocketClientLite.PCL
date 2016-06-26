using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ISocketLite.PCL.Interface;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Model;
using static WebsocketClientLite.PCL.Helper.WebsocketMasking;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketSenderService
    {
        private readonly ITcpSocketClient _client;

        internal WebsocketSenderService(ITcpSocketClient client)
        {
            _client = client;
        }

        internal async Task SendTextAsync(string message)
        {
            var msgAsBytes = Encoding.UTF8.GetBytes(message);

            await SendFrameAsync(msgAsBytes, FrameType.Single);
        }

        internal async Task SendTextAsync(string[] messageList)
        {

            if (messageList.Length < 1) return;

            if (messageList.Length == 1)
            {
                await SendFrameAsync(Encoding.UTF8.GetBytes(messageList[0]), FrameType.Single);
            }

            await SendFrameAsync(Encoding.UTF8.GetBytes(messageList[0]), FrameType.FirstOfMultipleFrames);

            for (var i = 1; i < messageList.Length - 1; i++)
            {
                await SendFrameAsync(Encoding.UTF8.GetBytes(messageList[i]), FrameType.Continuation);
            }
            await SendFrameAsync(Encoding.UTF8.GetBytes(messageList.Last()), FrameType.LastInMultipleFrames);
        }

        private bool _isSendingMultipleFrames = false;

        internal async Task SendTextMultiFrameAsync(string message, FrameType frameType)
        {
            if (_isSendingMultipleFrames)
            {
                if (frameType == FrameType.FirstOfMultipleFrames || frameType == FrameType.Single)
                {
                    await SendFrameAsync(Encoding.UTF8.GetBytes("_sequence aborted error_"), FrameType.LastInMultipleFrames);
                    _isSendingMultipleFrames = false;
                    throw new Exception("Multiple frames is progress. Frame must be a Continuation Frame or Last Frams in sequence. Multiple frame sequence aborted and finalized");
                }
            }

            if (!_isSendingMultipleFrames && frameType != FrameType.FirstOfMultipleFrames)
            {
                if (frameType == FrameType.Continuation || frameType == FrameType.LastInMultipleFrames)
                {
                    throw new Exception("Multiple frames sequence is not in initiated. Frame cannot be of a Continuation Frame or a Last Frame type");
                }
            }

            switch (frameType)
            {
                case FrameType.Single:
                    await SendFrameAsync(Encoding.UTF8.GetBytes(message), FrameType.Single);
                    break;
                case FrameType.FirstOfMultipleFrames:
                    _isSendingMultipleFrames = true;
                    await SendFrameAsync(Encoding.UTF8.GetBytes(message), FrameType.FirstOfMultipleFrames);
                    break;
                case FrameType.LastInMultipleFrames:
                    await SendFrameAsync(Encoding.UTF8.GetBytes(message), FrameType.LastInMultipleFrames);
                    _isSendingMultipleFrames = false;
                    break;
                case FrameType.Continuation:
                    await SendFrameAsync(Encoding.UTF8.GetBytes(message), FrameType.Continuation);
                    break;
            }
        }

        internal async Task SendCloseHandshake(StatusCodes statusCode)
        {
            var closeFrameBodyCode = BitConverter.GetBytes((ushort)statusCode);
            var reason = Encoding.UTF8.GetBytes(statusCode.ToString());

            await SendFrameAsync(closeFrameBodyCode.Concat(reason).ToArray(),
                FrameType.CloseControlFrame);
        }



        private async Task SendFrameAsync(byte[] content, FrameType frameType)
        {
            var firstByte = new byte[1];

            switch (frameType)
            {
                case FrameType.Single:
                    firstByte[0] = 129;
                    break;
                case FrameType.FirstOfMultipleFrames:
                    firstByte[0] = 1;
                    break;
                case FrameType.Continuation:
                    firstByte[0] = 0;
                    break;
                case FrameType.LastInMultipleFrames:
                    firstByte[0] = 128;
                    break;
                case FrameType.CloseControlFrame:
                    firstByte[0] = 136;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(frameType), frameType, null);
            }

            var payloadBytes = CreatePayloadBytes(content.Length, isMasking: true);
            var maskKey = CreateMaskKey();
            var maskedMessage = Encode(content, maskKey);
            var frame = firstByte.Concat(payloadBytes).Concat(maskKey).Concat(maskedMessage).ToArray();


            await _client.WriteStream.WriteAsync(frame, 0, frame.Length);
            await _client.WriteStream.FlushAsync();
        }
    }
}
