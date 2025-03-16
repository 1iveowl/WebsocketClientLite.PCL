using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Service;
using static WebsocketClientLite.Helper.WebsocketMasking;

namespace WebsocketClientLite.Model;

internal record Dataframe : IDataframe
{
    private readonly TcpConnectionService _tcpConnection;
    private readonly CancellationToken _ct;
    private byte[]? _data;
    private string? _message;

    internal bool FIN { get; init; }
    internal bool RSV1 { get; init; }
    internal bool RSV2 { get; init; }
    internal bool RSV3 { get; init; }
    internal bool MASK { get; init; }
    internal byte[]? MaskingBytes { get; init; }
    internal FragmentKind Fragment { get; init; }
    internal OpcodeKind Opcode { get; init; }
    internal MemoryStream? DataStream { get; init; }

    public string? Message => GetMessage();

    public byte[]? Binary => GetBinary();

    internal PayloadBitLengthKind PayloadBitLength { get; init; }

    internal ulong Length { get; init; }

    public Dataframe(TcpConnectionService tcpConnection, CancellationToken ct)
    {
        _tcpConnection = tcpConnection;
        _ct = ct;
    }

    internal async Task<byte[]?> GetNextBytes(ulong size) => await _tcpConnection.ReadBytesFromStream(size, _ct);

    private byte[]? GetBinary()
    {
        if (_data is null)
        {
            if (DataStream is not null)
            {
                _data = DataStream.ToArray();

                if (MASK)
                {
                    if (MaskingBytes is null)
                    {
                        throw new WebsocketClientLiteException("Unable to decode message. Masking byte cannot be null when masking is required.");
                    }
                    else
                    {
                        _data = Decode(_data, MaskingBytes);
                    }                        
                }
            }
            else
            {
                _data = null;
            }
        }

        return _data;
    }

    private string? GetMessage()
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
