using System;
using System.Diagnostics;
using System.Text;
using ISocketLite.PCL.Interface;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;
using static WebsocketClientLite.PCL.Helper.WebsocketMasking;

namespace WebsocketClientLite.PCL.Parser
{
    internal class TextDataParser : IDisposable
    {
        private readonly ControlFrameHandler _controlFrameHandler;

        private PayloadLenghtType _payloadLenghtTypeInBits;
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

        private byte[] _payloadLenghtByteArray;
        private byte[] _maskKey;
        private byte[] _contentByteArray;

        private ulong _payloadLenght;
        private ulong _contentPosition;

        internal bool IsCloseRecieved { get; private set; }

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

        internal TextDataParser()
        {
            _controlFrameHandler = new ControlFrameHandler();

        }

        internal void Reinitialize()
        {
            IsCloseRecieved = false;
        }

        internal void Parse(ITcpSocketClient tcpSocketClient, byte data, bool excludeZeroApplicationDataInPong = false)
        {
            if (!_isFrameBeingReceived)
            {
                if (IsControlFrame(tcpSocketClient, data, excludeZeroApplicationDataInPong))
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

        private bool IsControlFrame(
            ITcpSocketClient tcpSocketClient, 
            byte data, 
            bool excludeZeroApplicationDataInPong = false)
        {
            var controlFrame = _controlFrameHandler.CheckForPingOrCloseControlFrame(tcpSocketClient, data, excludeZeroApplicationDataInPong);

            switch (controlFrame)
            {
                case ControlFrameType.Ping:
                    return true;
                case ControlFrameType.Close:
                    IsCloseRecieved = true;
                    return true;
                case ControlFrameType.Pong:
                    return true;
                case ControlFrameType.None:
                    return false;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void HandleContent(byte data)
        {
            if (_contentPosition < _payloadLenght - 1)
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
                        _newMessage = _newMessage + frameContent;
                        HasNewMessage = false;
                        break;

                    case FrameType.Continuation:
                        _newMessage = _newMessage + frameContent;
                        HasNewMessage = false;
                        break;

                    case FrameType.LastInMultipleFrames:
                        _newMessage = _newMessage + frameContent;
                        _isMultiFrameContent = false;
                        HasNewMessage = true;
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                _isFrameBeingReceived = false;
                _payloadLenghtTypeInBits = PayloadLenghtType.Unspecified;
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

            HandlePayloadLenght(data);
        }

        private void HandlePayloadLenght(byte data)
        {
            if (_payloadLengthPosition >= 1)
            {
                AddByteToPayloadLenght(data);
            }
            else
            {
                AddByteToPayloadLenght(data);
                InitializeContentRead();
            }
        }

        private void AddByteToPayloadLenght(byte data)
        {
            _payloadLenghtByteArray[_payloadLengthPosition] = data;
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
                _payloadLenghtTypeInBits = PayloadLenghtType.Bits8;
                _payloadLengthPosition = 0;

                _payloadLenghtByteArray = new byte[1] { (byte)firstByteLength };
                InitializeContentRead();
            }
            else
                switch (firstByteLength)
                {
                    case 126:
                        _payloadLenghtTypeInBits = PayloadLenghtType.Bits16;

                        _payloadLenghtByteArray = new byte[2];
                        _payloadLengthPosition = 1;
                        break;
                    case 127:
                        _payloadLenghtTypeInBits = PayloadLenghtType.Bits64;
                        _payloadLengthPosition = 7;

                        _payloadLenghtByteArray = new byte[8];
                        break;
                    default:
                        break;
                }

            //_payloadLengthPosition = 0;
            _isFirstPayloadByte = false;
        }

        private void InitializeContentRead()
        {
            switch (_payloadLenghtTypeInBits)
            {
                case PayloadLenghtType.Bits8:
                    _payloadLenght = _payloadLenghtByteArray[0];
                    break;
                case PayloadLenghtType.Bits16:
                    _payloadLenght = BitConverter.ToUInt16(_payloadLenghtByteArray, 0);
                    break;
                case PayloadLenghtType.Bits64:
                    _payloadLenght = BitConverter.ToUInt64(_payloadLenghtByteArray, 0);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            _contentByteArray = new byte[_payloadLenght];
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
