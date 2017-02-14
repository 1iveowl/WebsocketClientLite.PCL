using System.Diagnostics;
using System.Threading.Tasks;
using ISocketLite.PCL.Interface;
using WebsocketClientLite.PCL.Model;

namespace WebsocketClientLite.PCL.Helper
{
    internal class ControlFrameHandler
    {
        private bool IsPingReceived { get; set; }

        private bool IsCloseReceived { get; set; }

        private byte[] _pong;

        private bool _isReceivingPingData = false;
        private int _payloadLength = 0;
        private int _payloadPosition = 0;
        private bool _isNextBytePayloadLength = false;

        internal ControlFrameHandler()
        {
        }

        internal ControlFrameType CheckForPingOrCloseControlFrame(ITcpSocketClient tcpSocketClient, byte data)
        {
            if (_isReceivingPingData)
            {
                AddPingPayload(tcpSocketClient, data);
                return ControlFrameType.Ping;
            }

            switch (data)
            {
                case 136:
                    IsCloseReceived = true;
                    return ControlFrameType.Close;
                case 137:
                    InitPingStart();
                    return ControlFrameType.Ping;
            }
            return ControlFrameType.None;
        }

        private void AddPingPayload(ITcpSocketClient tcpSocketClient, byte data)
        {
            if (_isNextBytePayloadLength)
            {
                var b = data;
                if (b == 0)
                {
                    ReinitializePing();
                    _pong = new byte[1] {138};
                    SendPong(tcpSocketClient);
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
                    SendPong(tcpSocketClient);
                }
            }
        }

        private void SendPong(ITcpSocketClient tcpSocketClient)
        {
            Task.Run(async () => await SendPongAsync(tcpSocketClient)).ConfigureAwait(false);
        }

        private void InitPingStart()
        {
            Debug.WriteLine("Ping received");
            IsPingReceived = true;
            _isReceivingPingData = true;
            _isNextBytePayloadLength = true;
        }

        private void ReinitializePing()
        {
            IsPingReceived = false;
            _isReceivingPingData = false;
            _isNextBytePayloadLength = false;
        }

        private async Task SendPongAsync(ITcpSocketClient tcpSocketClient)
        {
            await tcpSocketClient.WriteStream.WriteAsync(_pong, 0, _pong.Length);
            await tcpSocketClient.WriteStream.FlushAsync();
            Debug.WriteLine("Pong send");
        }
    }
}
