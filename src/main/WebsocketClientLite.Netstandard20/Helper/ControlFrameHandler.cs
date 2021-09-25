using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using WebsocketClientLite.PCL.Model;

namespace WebsocketClientLite.PCL.Helper
{
    internal class ControlFrameHandler
    {
        private byte[] _pong;

        private bool _isReceivingPingData = false;
        private int _payloadLength = 0;
        private int _payloadPosition = 0;
        private bool _isNextBytePayloadLength = false;

        internal ControlFrameHandler()
        {

        }

        internal async Task<ControlFrameType> CheckForPingOrCloseControlFrameAsync(
            Stream tcpSocketClient, 
            byte data, 
            bool excludeZeroApplicationDataInPong = false)
        {
            if (_isReceivingPingData)
            {
                await AddPingPayload(tcpSocketClient, data, excludeZeroApplicationDataInPong);
                return ControlFrameType.Ping;
            }

            switch (data)
            {
                case 136:
                    return ControlFrameType.Close;
                case 137:
                    InitPingStart();
                    return ControlFrameType.Ping;
            }
            return ControlFrameType.None;
        }

        private async Task AddPingPayload(Stream tcpStream, byte data, bool excludeZeroApplicationDataInPong = false)
        {
            if (_isNextBytePayloadLength)
            {
                var b = data;
                if (b == 0)
                {
                    ReinitializePing();

                    _pong = excludeZeroApplicationDataInPong 
                        ? new byte[1] { 138} 
                        : new byte[2] { 138, 0 };
                    
                    await SendPongAsync(tcpStream);
                }
                else
                {
                    _payloadLength = b >> 1;
                    Debug.WriteLine($"Ping payload lenght: {_payloadLength}");
                    _pong = new byte[_payloadLength];
                    _pong[0] = 138;
                    _payloadPosition = 1;
                    _isNextBytePayloadLength = false;
                }
            }
            else
            {
                if (_payloadPosition < _payloadLength)
                {
                    Debug.WriteLine("Ping payload received");
                    _pong[_payloadPosition] = data;
                    _payloadPosition++;
                }
                else
                {
                    ReinitializePing();
                    await SendPongAsync(tcpStream);
                }
            }
        }
        private void InitPingStart()
        {
            Debug.WriteLine("Ping received");
            _isReceivingPingData = true;
            _isNextBytePayloadLength = true;
        }

        private void ReinitializePing()
        {
            _isReceivingPingData = false;
            _isNextBytePayloadLength = false;
        }

        private async Task SendPongAsync(Stream tcpStream)
        {
            await tcpStream.WriteAsync(_pong, 0, _pong.Length);
            await tcpStream.FlushAsync();
            Debug.WriteLine("Pong send");
        }
    }
}
