using System.Text;
using IWebsocketClientLite;

namespace WebsocketClientLite.Model;

internal record Dataframe : IDataframe
{
    private string? _message;

    internal bool FIN { get; init; }
    internal bool RSV1 { get; init; }
    internal bool RSV2 { get; init; }
    internal bool RSV3 { get; init; }
    internal bool MASK { get; init; }
    internal FragmentKind Fragment { get; init; }
    internal OpcodeKind Opcode { get; init; }
    internal ulong Length { get; init; }

    /// <summary>
    /// The frame payload, already unmasked. Empty for a zero-length frame.
    /// </summary>
    internal byte[]? Payload { get; init; }

    public byte[]? Binary => Payload;

    public string? Message => GetMessage();

    private string? GetMessage()
    {
        if (_message is null && Payload is not null && Opcode is OpcodeKind.Text)
        {
            _message = Encoding.UTF8.GetString(Payload, 0, Payload.Length);
        }

        return _message;
    }
}
