using System.Text;
using IWebsocketClientLite;

namespace WebsocketClientLite.Model;

internal record Dataframe : IDataframe
{
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

    // Computed on demand rather than cached in a field: a cached value would be
    // carried along by `with` clones (e.g. fragment reassembly replacing Payload)
    // and could go stale relative to the new payload.
    public string? Message =>
        Payload is not null && Opcode is OpcodeKind.Text
            ? Encoding.UTF8.GetString(Payload, 0, Payload.Length)
            : null;
}
