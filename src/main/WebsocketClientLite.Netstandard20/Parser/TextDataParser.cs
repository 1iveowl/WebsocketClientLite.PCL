using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;
using static WebsocketClientLite.PCL.Helper.WebsocketMasking;

namespace WebsocketClientLite.PCL.Parser
{
    internal class TextDataParser : IDisposable
    {
        private readonly ControlFrameHandler _controlFrameHandler;

        private PayloadLenghtType _payloadLengthTypeInBits;
        private FrameType _frameType;

        private bool _isFrameBeingReceived;
        private bool _isMsgMasked;
        private bool _isNextByteMaskingByte;
        private bool _isNextBytePayloadLengthByte;
        private bool _isFirstPayloadByte;
        private bool _isMultiFrameContent;

        private bool _isNextByteContent;

        private byte _payloadLengthPosition;
        private byte _maskBytePosition;

        private byte[] _payloadLengthByteArray;
        private byte[] _maskKey;
        private byte[] _contentByteArray;

        private ulong _payloadLength;
        private ulong _contentPosition;

        internal bool IsCloseReceived { get; private set; }

        private string _newMessage;

        internal string NewMessage
        {
            get
            {
                HasNewMessage = false;
                return _newMessage;
            }
        }

        internal bool HasNewMessage { get; private set; }

        internal TextDataParser(ControlFrameHandler controlFrameHandler)
        {
            _controlFrameHandler = controlFrameHandler;
        }

        //internal TextDataParser(
        //        Func<Stream, byte[], CancellationToken, Task> writeFunc)
        //{
        //    _controlFrameHandler = new ControlFrameHandler(writeFunc);
        //}

        internal void Reinitialize()
        {
            IsCloseReceived = false;
        }

        internal async Task Parse(
            Stream stream, 
            byte data,
            CancellationToken ct,
            bool excludeZeroApplicationDataInPong = false)
        {
            if (!_isFrameBeingReceived)
            {
                if (await IsControlFrame(
                    stream, 
                    data, 
                    ct,
                    excludeZeroApplicationDataInPong))
                {
                    return;
                }

                ListenForFrameStartByte(data);
                return;
            }

            if (_isNextBytePayloadLengthByte)
            {
                HandlePayloadLength(data);
                return;
            }

            if (_isNextByteMaskingByte)
            {
                HandleMaskKey(data);
                return;
            }

            if (_isNextByteContent)
            {
                HandleContent(data);
            }
        }

        private async Task<bool> IsControlFrame(
            Stream stream,
            byte data,
            CancellationToken ct,
            bool excludeZeroApplicationDataInPong = false) =>            
                await _controlFrameHandler.CheckForControlFrame(
                        stream,
                        data,
                        ct,
                        excludeZeroApplicationDataInPong) switch
                {
                    ControlFrameType.Ping => true,
                    ControlFrameType.Pong => true,
                    ControlFrameType.Close => true,
                    ControlFrameType.None => false,
                    _ => throw new ArgumentOutOfRangeException($"Unknown control frame type."),
                };

        private void HandleContent(byte data)
        {
            if (_contentPosition < _payloadLength - 1)
            {
                _contentByteArray[_contentPosition] = data;
                _contentPosition++;
            }
            else
            {
                _contentByteArray[_contentPosition] = data;
                string frameContent;

                if (_isMsgMasked)
                {
                    var decoded = Decode(_contentByteArray, _maskKey);
                    frameContent = Encoding.UTF8.GetString(decoded, 0, decoded.Length);
                }
                else
                {
                    frameContent = Encoding.UTF8.GetString(_contentByteArray, 0, _contentByteArray.Length);
                }

                switch (_frameType)
                {
                    case FrameType.Single:
                        _newMessage = frameContent;
                        HasNewMessage = true;
                        break;

                    case FrameType.FirstOfMultipleFrames:
                        _newMessage += frameContent;
                        HasNewMessage = false;
                        break;

                    case FrameType.Continuation:
                        _newMessage += frameContent;
                        HasNewMessage = false;
                        break;

                    case FrameType.LastInMultipleFrames:
                        _newMessage += frameContent;
                        _isMultiFrameContent = false;
                        HasNewMessage = true;
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                _isFrameBeingReceived = false;
                _payloadLengthTypeInBits = PayloadLenghtType.Unspecified;
            }
        }

        private void HandleMaskKey(byte data)
        {
            if (_maskBytePosition <= 3)
            {
                _maskKey[_maskBytePosition] = data;
                _maskBytePosition++;
            }
            else
            {
                _isNextByteMaskingByte = false;
                _isNextByteContent = true;
            }
        }

        private void HandlePayloadLength(byte data)
        {
            if (_isFirstPayloadByte)
            {
                HandleFirstPayloadByte(data);
                return;
            }

            if (_payloadLengthPosition >= 1)
            {
                AddByteToPayloadLength(data);
            }
            else
            {
                AddByteToPayloadLength(data);
                InitializeContentRead();
            }
        }

        private void AddByteToPayloadLength(byte data)
        {
            _payloadLengthByteArray[_payloadLengthPosition] = data;
            _payloadLengthPosition--;
        }

        private void HandleFirstPayloadByte(byte data)
        {
            int firstByteLength;

            if (data >= 128)
            {
                _isMsgMasked = true;
                firstByteLength = data - 128;
            }
            else
            {
                firstByteLength = data;
            }

            if (firstByteLength <= 125)
            {
                _payloadLengthTypeInBits = PayloadLenghtType.Bits8;
                _payloadLengthPosition = 0;

                _payloadLengthByteArray = new byte[1] { (byte)firstByteLength };
                InitializeContentRead();
            }
            else
                switch (firstByteLength)
                {
                    case 126:
                        _payloadLengthTypeInBits = PayloadLenghtType.Bits16;

                        _payloadLengthByteArray = new byte[2];
                        _payloadLengthPosition = 1;
                        break;
                    case 127:
                        _payloadLengthTypeInBits = PayloadLenghtType.Bits64;
                        _payloadLengthPosition = 7;

                        _payloadLengthByteArray = new byte[8];
                        break;
                    default:
                        break;
                }
            _isFirstPayloadByte = false;
        }

        private void InitializeContentRead()
        {
            _payloadLength = _payloadLengthTypeInBits switch
            {
                PayloadLenghtType.Bits8 => _payloadLengthByteArray[0],
                PayloadLenghtType.Bits16 => BitConverter.ToUInt16(_payloadLengthByteArray, 0),
                PayloadLenghtType.Bits64 => BitConverter.ToUInt64(_payloadLengthByteArray, 0),
                _ => throw new ArgumentOutOfRangeException(),
            };

            _contentByteArray = new byte[_payloadLength];
            _contentPosition = 0;

            _isNextBytePayloadLengthByte = false;
            _isNextByteMaskingByte = _isMsgMasked;

            if (_isMsgMasked)
            {
                _maskBytePosition = 0;
                _maskKey = new byte[4];
            }
            else
            {
                _isNextByteContent = true;
            }
        }

        private void ListenForFrameStartByte(byte data)
        {
            switch (data)
            {
                //Single frame sequence detected
                case 129:
                    _frameType = FrameType.Single;

                    _newMessage = null;
                    InitFrameMessage();
                    break;

                // Final frame in sequence detected
                case 128:
                    // Only accept final frame in sequence if its part of a multi frame sequence 
                    if (_isMultiFrameContent)
                    {
                        _frameType = FrameType.LastInMultipleFrames;

                        InitFrameMessage();
                    }
                    break;
                // Multi frame sequence detected
                case 1:
                    _isMultiFrameContent = true;
                    _frameType = FrameType.FirstOfMultipleFrames;

                    _newMessage = null;
                    InitFrameMessage();
                    break;

                case 0:
                    // Only accept continuation frames if a multi frame content sequence is detected
                    if (_isMultiFrameContent)
                    {
                        _frameType = FrameType.Continuation;
                        InitFrameMessage();
                    }
                    break;
            }
        }
        private void InitFrameMessage()
        {
            _isFrameBeingReceived = true;
            _isFirstPayloadByte = true;
            _isNextBytePayloadLengthByte = true;
        }
        public void Dispose()
        {

        }
    }
}
