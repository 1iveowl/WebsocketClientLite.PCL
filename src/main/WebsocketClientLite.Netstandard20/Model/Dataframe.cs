using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Service;
using static WebsocketClientLite.PCL.Helper.WebsocketMasking;

namespace WebsocketClientLite.PCL.Model
{
    internal record Dataframe : IDataframe
    {
        private readonly TcpConnectionService _tcpConnection;
        private readonly CancellationToken _ct;
        private byte[] _data;
        private string _message;

        internal bool FIN { get; init; }
        internal bool RSV1 { get; init; }
        internal bool RSV2 { get; init; }
        internal bool RSV3 { get; init; }
        internal bool MASK { get; init; }
        internal byte[] MaskingBytes { get; init; }
        internal FragmentKind Fragment { get; init; }
        internal OpcodeKind Opcode { get; init; }
        internal MemoryStream DataStream { get; init; }

        public string Message => GetMessage();

        public byte[] Binary => GetBinary();

        public PayloadBitLengthKind PayloadBitLength { get; init; }

        public ulong Length { get; init; }

        public Dataframe(TcpConnectionService tcpConnection, CancellationToken ct)
        {
            _tcpConnection = tcpConnection;
            _ct = ct;
        }

        internal async Task<byte[]> GetNextBytes(ulong size)
        {
            return await _tcpConnection.ReadBytesFromStream(size, _ct);
        }

        private byte[] GetBinary()
        {
            if (_data is null)
            {
                if (DataStream is not null)
                {
                    _data = DataStream.ToArray();

                    if (MASK)
                    {
                        _data = Decode(_data, MaskingBytes);
                    }
                }
                else
                {
                    _data = null;
                }

            }

            return _data;
        }

        private string GetMessage()
        {
            if (_message is null)
            {
                var data = GetBinary();

                if (data is not null)
                {

                    _message = Opcode is OpcodeKind.Text
                        ? Encoding.UTF8.GetString(data, 0, data.Length)
                        : default;
                }
                else
                {
                    _message = null;
                }

            }            

            return _message;            
        }
    }
}
