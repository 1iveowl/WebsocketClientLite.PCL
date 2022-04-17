//using IWebsocketClientLite.PCL;
//using System;
//using System.Diagnostics;
//using System.IO;
//using System.Threading;
//using System.Threading.Tasks;

//namespace WebsocketClientLite.PCL.Service
//{
//    internal class ControlFrameHandler
//    {
//        private readonly Func<Stream, byte[], CancellationToken, Task> _writeFunc;

//        private byte[] _pong;
//        private bool _isReceivingPingData = false;
//        private int _payloadLength = 0;
//        private int _payloadPosition = 0;
//        private bool _isNextBytePayloadLength = false;

//        internal ControlFrameHandler(Func<Stream, byte[], CancellationToken, Task> writeFunc)
//        {
//            _writeFunc = writeFunc;
//        }

//        internal async Task<OpcodeKind> GetControlFrame(
//            Stream stream,
//            byte @byte,
//            CancellationToken ct,
//            bool excludeZeroApplicationDataInPong = false)
//        {
//            if (_isReceivingPingData)
//            {
//                await AddPingPayload(
//                    stream,
//                    @byte,
//                    ct,
//                    excludeZeroApplicationDataInPong);

//                return OpcodeKind.Ping;
//            }

//            switch (@byte)
//            {
//                case 136:
//                    return OpcodeKind.Close;
//                case 137:
//                    InitPingStart();
//                    return OpcodeKind.Ping;
//            }
//            return OpcodeKind.None;
//        }

//        internal async Task SendPing(
//            Stream stream,
//            CancellationToken ct,
//            bool isExcludingZeroApplicationDataInPong = false)
//        {
//            var ping = isExcludingZeroApplicationDataInPong
//                        ? new byte[1] { 137 }
//                        : new byte[2] { 137, 0 };

//            await SendAsync(stream, ping, ct, "Ping");
//        }

//        private async Task AddPingPayload(
//            Stream stream,
//            byte data,
//            CancellationToken ct,
//            bool isExcludingZeroApplicationDataInPong = false)
//        {
//            if (_isNextBytePayloadLength)
//            {
//                var b = data;

//                if (b == 0)
//                {
//                    ReinitializePing();

//                    _pong = isExcludingZeroApplicationDataInPong
//                        ? new byte[1] { 138 }
//                        : new byte[2] { 138, 0 };

//                    await SendAsync(stream, _pong, ct);
//                }
//                else
//                {
//                    _payloadLength = b >> 1;
//                    Debug.WriteLine($"Ping payload lenght: {_payloadLength}");
//                    _pong = new byte[_payloadLength];
//                    _pong[0] = 138;
//                    _payloadPosition = 1;
//                    _isNextBytePayloadLength = false;
//                }
//            }
//            else
//            {
//                if (_payloadPosition < _payloadLength)
//                {
//                    Debug.WriteLine("Ping payload received");
//                    _pong[_payloadPosition] = data;
//                    _payloadPosition++;
//                }
//                else
//                {
//                    ReinitializePing();
//                    await SendAsync(stream, _pong, ct);
//                }
//            }
//        }
//        private void InitPingStart()
//        {
//            Debug.WriteLine("Ping received");
//            _isReceivingPingData = true;
//            _isNextBytePayloadLength = true;
//        }

//        private void ReinitializePing()
//        {
//            _isReceivingPingData = false;
//            _isNextBytePayloadLength = false;
//        }

//        private async Task SendAsync(Stream stream, byte[] pp, CancellationToken ct, string pingpong = "Pong")
//        {
//            await _writeFunc(stream, pp, ct);
//            Debug.WriteLine($"{pingpong} send.");
//        }
//    }
//}
