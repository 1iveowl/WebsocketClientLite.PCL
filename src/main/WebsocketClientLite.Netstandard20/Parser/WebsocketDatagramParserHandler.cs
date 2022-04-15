using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Service;
using static WebsocketClientLite.PCL.Helper.WebsocketMasking;

namespace WebsocketClientLite.PCL.Parser
{
    internal class WebsocketDatagramParserHandler //: IDisposable
    {
        private readonly ControlFrameHandler _controlFrameHandler;

        private PayloadBitLengthKind _payloadLengthTypeInBits;
        private OpcodeKind _frameType;
        private WebsocketParserStateKind _parserState = WebsocketParserStateKind.FirstByte;

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

        internal WebsocketDatagramParserHandler(ControlFrameHandler controlFrameHandler)
        {
            _controlFrameHandler = controlFrameHandler;
        }

        internal void Reinitialize()
        {
            IsCloseReceived = false;
        }

        //internal WebsocketDatagram CreateDatagram(byte @byte)
        //{
        //    var datagram = new WebsocketDatagram
        //    {
        //        WebsocketParserState = WebsocketParserStateKind.FirstByte
        //    };

        //    return (FrameTypeKind)@byte switch
        //    {
        //        FrameTypeKind.Text => datagram with { Kind = WebsocketDatagramKind.Text },
        //        FrameTypeKind.Binary => datagram with { Kind = WebsocketDatagramKind.Binary },

        //        FrameTypeKind.Continuation => throw new NotImplementedException(),

        //        FrameTypeKind.FirstOfMultipleFrames => throw new NotImplementedException(),
        //        FrameTypeKind.LastInMultipleFrames => throw new NotImplementedException(),
        //        FrameTypeKind.Ping => throw new NotImplementedException(),
        //        FrameTypeKind.Pong => throw new NotImplementedException(),
        //        FrameTypeKind.Close => throw new NotImplementedException(),
        //        FrameTypeKind.None => throw new NotImplementedException(),
        //        _ => throw new NotImplementedException()
        //    };
        //}

        ////internal WebsocketDatagram GetPayloadLength(Tuple<(WebsocketDatagram datagram, byte @byte)> tuple)
        //internal WebsocketDatagram GetPayloadLength(WebsocketDatagram datagram, byte @byte)
        //{

        //    if (@byte <= 125)
        //    {
        //        return datagram with { Length = @byte, WebsocketParserState = WebsocketParserStateKind.PayloadByte7Bit };
        //    }
        //    if (@byte == 126)
        //    {
        //        return datagram with { LengthBytes = new byte[2], WebsocketParserState = WebsocketParserStateKind.PayloadByte16Bit };
        //    }
        //    if (@byte >= 127)
        //    {
        //        return datagram with { LengthBytes = new byte[8], WebsocketParserState = WebsocketParserStateKind.PayloadByte64Bit };
        //    }
        //    else
        //    {
        //        throw new NotImplementedException();
        //    }
        //}

        //    internal async Task<WebsocketDatagram> Parse(
        //        Stream stream, 
        //        byte @byte,
        //        WebsocketDatagram datagram,
        //        CancellationToken ct,
        //        bool excludeZeroApplicationDataInPong = false)
        //    {
        //        if (datagram.WebsocketParserState == WebsocketParserStateKind.Start)
        //        {
        //            return (FrameTypeKind)@byte switch
        //            {
        //                FrameTypeKind.Text => datagram with
        //                {
        //                    Kind = WebsocketDatagramKind.Text,
        //                    WebsocketParserState = WebsocketParserStateKind.FirstByte
        //                },

        //                FrameTypeKind.Continuation => throw new NotImplementedException(),
        //                FrameTypeKind.Binary => throw new NotImplementedException(),
        //                FrameTypeKind.FirstOfMultipleFrames => throw new NotImplementedException(),
        //                FrameTypeKind.LastInMultipleFrames => throw new NotImplementedException(),
        //                FrameTypeKind.Ping => throw new NotImplementedException(),
        //                FrameTypeKind.Pong => throw new NotImplementedException(),
        //                FrameTypeKind.Close => throw new NotImplementedException(),
        //                FrameTypeKind.None => throw new NotImplementedException(),
        //                _ => throw new NotImplementedException()
        //            };
        //        }

        //        if (datagram.WebsocketParserState == WebsocketParserStateKind.FirstByte)
        //        {
        //            if (@byte <= 125)
        //            {
        //                return datagram with { Length = @byte, WebsocketParserState = WebsocketParserStateKind.PayloadByte7Bit };
        //            }
        //            if (@byte == 126)
        //            {
        //                return datagram with { LengthBytes = new byte[2], WebsocketParserState = WebsocketParserStateKind.PayloadByte16Bit};
        //            }
        //            if (@byte >=127)
        //            {
        //                return datagram with { LengthBytes = new byte[8], WebsocketParserState = WebsocketParserStateKind.PayloadByte64Bit };
        //            }
        //            else
        //            {
        //                throw new NotImplementedException();
        //            }

        //            //HandlePayloadLength(@byte);
        //        }

        //        if (datagram.Length == 0 && 
        //            (datagram.WebsocketParserState == WebsocketParserStateKind.PayloadByte16Bit
        //            || datagram.WebsocketParserState == WebsocketParserStateKind.PayloadByte64Bit))
        //        {
        //            if (datagram.WebsocketParserState == WebsocketParserStateKind.PayloadByte16Bit)
        //            {
        //                return GetLenght(2, datagram.LengthBytes, bytes => BitConverter.ToUInt16(bytes, 0));
        //            }

        //            if (datagram.WebsocketParserState == WebsocketParserStateKind.PayloadByte16Bit)
        //            {
        //                return GetLenght(8, datagram.LengthBytes, bytes => BitConverter.ToUInt64(bytes, 0));
        //            }


        //        WebsocketDatagram GetLenght(int arrayLength, byte[] bytes, Func<byte[], ulong> convertAction)
        //        {
        //            var lenBytes = new byte[arrayLength];
        //            datagram.LengthBytes.CopyTo(lenBytes, 0);
        //            lenBytes[datagram.ByteCount] = @byte;

        //            return datagram.ByteCount == arrayLength - 1
        //                ? datagram with
        //                {
        //                    LengthBytes = lenBytes,
        //                    ByteCount = datagram.ByteCount + 1
        //                }
        //                : datagram with
        //                {
        //                    Length = convertAction(datagram.LengthBytes)
        //                };
        //        }

        //        //Func<byte, Task> parseTask = @byte switch
        //        //{
        //        //    //(byte)FrameTypeKind.Text => default,

        //        //    _ => (a) => ListenForFrameStartByte(@byte),
        //        //};

        //        //if (@byte == (byte)FrameTypeKind.Text)
        //        //{

        //        //}

        //        //if (!_isFrameBeingReceived)
        //        //{
        //        //    if (await IsControlFrame(
        //        //        stream, 
        //        //        @byte, 
        //        //        ct,
        //        //        excludeZeroApplicationDataInPong))
        //        //    {
        //        //        return;
        //        //    }

        //        //    ListenForFrameStartByte(@byte);
        //        //    return;
        //        //}

        //        if (_isNextBytePayloadLengthByte)
        //        {
        //            HandlePayloadLength(@byte);
        //            return;
        //        }

        //        if (_isNextByteMaskingByte)
        //        {
        //            HandleMaskKey(@byte);
        //            return;
        //        }

        //        if (_isNextByteContent)
        //        {
        //            HandleContent(@byte);
        //        }
        //    }

        //    private async Task<bool> IsControlFrame(
        //        Stream stream,
        //        byte data,
        //        CancellationToken ct,
        //        bool excludeZeroApplicationDataInPong = false) =>            
        //            await _controlFrameHandler.GetControlFrame(
        //                    stream,
        //                    data,
        //                    ct,
        //                    excludeZeroApplicationDataInPong) switch
        //            {
        //                FrameTypeKind.Ping => true,
        //                FrameTypeKind.Pong => true,
        //                FrameTypeKind.Close => true,
        //                FrameTypeKind.None => false,
        //                _ => throw new ArgumentOutOfRangeException($"Invalid control frame type."),
        //            };

        //    private void HandleContent(byte data)
        //    {
        //        if (_contentPosition < _payloadLength - 1)
        //        {
        //            _contentByteArray[_contentPosition] = data;
        //            _contentPosition++;
        //        }
        //        else
        //        {
        //            _contentByteArray[_contentPosition] = data;
        //            string frameContent;

        //            if (_isMsgMasked)
        //            {
        //                var decoded = Decode(_contentByteArray, _maskKey);
        //                frameContent = Encoding.UTF8.GetString(decoded, 0, decoded.Length);
        //            }
        //            else
        //            {
        //                frameContent = Encoding.UTF8.GetString(_contentByteArray, 0, _contentByteArray.Length);
        //            }

        //            switch (_frameType)
        //            {
        //                case FrameTypeKind.Single:
        //                    _newMessage = frameContent;
        //                    HasNewMessage = true;
        //                    break;

        //                case FrameTypeKind.FirstOfMultipleFrames:
        //                    _newMessage += frameContent;
        //                    HasNewMessage = false;
        //                    break;

        //                case FrameTypeKind.Continuation:
        //                    _newMessage += frameContent;
        //                    HasNewMessage = false;
        //                    break;

        //                case FrameTypeKind.LastInMultipleFrames:
        //                    _newMessage += frameContent;
        //                    _isMultiFrameContent = false;
        //                    HasNewMessage = true;
        //                    break;

        //                default:
        //                    throw new ArgumentOutOfRangeException();
        //            }

        //            _isFrameBeingReceived = false;
        //            _payloadLengthTypeInBits = PayloadLenghtType.Unspecified;
        //        }
        //    }

        //    private void HandleMaskKey(byte data)
        //    {
        //        if (_maskBytePosition <= 3)
        //        {
        //            _maskKey[_maskBytePosition] = data;
        //            _maskBytePosition++;
        //        }
        //        else
        //        {
        //            _isNextByteMaskingByte = false;
        //            _isNextByteContent = true;
        //        }
        //    }

        //    private void HandlePayloadLength(byte data)
        //    {
        //        if (_isFirstPayloadByte)
        //        {
        //            HandleFirstPayloadByte(data);
        //            return;
        //        }

        //        if (_payloadLengthPosition >= 1)
        //        {
        //            AddByteToPayloadLength(data);
        //        }
        //        else
        //        {
        //            AddByteToPayloadLength(data);
        //            InitializeContentRead();
        //        }
        //    }

        //    private void AddByteToPayloadLength(byte data)
        //    {
        //        _payloadLengthByteArray[_payloadLengthPosition] = data;
        //        _payloadLengthPosition--;
        //    }

        //    private void HandleFirstPayloadByte(byte data)
        //    {
        //        int firstByteLength;

        //        if (data >= 128)
        //        {
        //            _isMsgMasked = true;
        //            firstByteLength = data - 128;
        //        }
        //        else
        //        {
        //            firstByteLength = data;
        //        }

        //        if (firstByteLength <= 125)
        //        {
        //            _payloadLengthTypeInBits = PayloadLenghtType.Bits8;
        //            _payloadLengthPosition = 0;

        //            _payloadLengthByteArray = new byte[1] { (byte)firstByteLength };
        //            InitializeContentRead();
        //        }
        //        else
        //            switch (firstByteLength)
        //            {
        //                case 126:
        //                    _payloadLengthTypeInBits = PayloadLenghtType.Bits16;

        //                    _payloadLengthByteArray = new byte[2];
        //                    _payloadLengthPosition = 1;
        //                    break;
        //                case 127:
        //                    _payloadLengthTypeInBits = PayloadLenghtType.Bits64;
        //                    _payloadLengthPosition = 7;

        //                    _payloadLengthByteArray = new byte[8];
        //                    break;
        //                default:
        //                    break;
        //            }
        //        _isFirstPayloadByte = false;
        //    }

        //    private void InitializeContentRead()
        //    {
        //        _payloadLength = _payloadLengthTypeInBits switch
        //        {
        //            PayloadLenghtType.Bits8 => _payloadLengthByteArray[0],
        //            PayloadLenghtType.Bits16 => BitConverter.ToUInt16(_payloadLengthByteArray, 0),
        //            PayloadLenghtType.Bits64 => BitConverter.ToUInt64(_payloadLengthByteArray, 0),
        //            _ => throw new ArgumentOutOfRangeException(),
        //        };

        //        _contentByteArray = new byte[_payloadLength];
        //        _contentPosition = 0;

        //        _isNextBytePayloadLengthByte = false;
        //        _isNextByteMaskingByte = _isMsgMasked;

        //        if (_isMsgMasked)
        //        {
        //            _maskBytePosition = 0;
        //            _maskKey = new byte[4];
        //        }
        //        else
        //        {
        //            _isNextByteContent = true;
        //        }
        //    }

        //    private void ListenForFrameStartByte(byte @byte)
        //    {
        //        switch (@byte)
        //        {
        //            //Single frame sequence detected
        //            case 129:
        //                _frameType = FrameTypeKind.Single;

        //                _newMessage = null;
        //                InitFrameMessage();
        //                break;

        //            // Final frame in sequence detected
        //            case 128:
        //                // Only accept final frame in sequence if its part of a multi frame sequence 
        //                if (_isMultiFrameContent)
        //                {
        //                    _frameType = FrameTypeKind.LastInMultipleFrames;

        //                    InitFrameMessage();
        //                }
        //                break;
        //            // Multi frame sequence detected
        //            case 1:
        //                _isMultiFrameContent = true;
        //                _frameType = FrameTypeKind.FirstOfMultipleFrames;

        //                _newMessage = null;
        //                InitFrameMessage();
        //                break;

        //            case 0:
        //                // Only accept continuation frames if a multi frame content sequence is detected
        //                if (_isMultiFrameContent)
        //                {
        //                    _frameType = FrameTypeKind.Continuation;
        //                    InitFrameMessage();
        //                }
        //                break;
        //        }
        //    }
        //    private void InitFrameMessage()
        //    {
        //        _isFrameBeingReceived = true;
        //        _isFirstPayloadByte = true;
        //        _isNextBytePayloadLengthByte = true;
        //    }
        //    public void Dispose()
        //    {

        //    }
        //}
    }
}
