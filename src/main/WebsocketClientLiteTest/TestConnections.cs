using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WebsocketClientLite.Helper;
using WebsocketClientLite.Model;
using WebsocketClientLite.Service;

namespace WebsocketClientLiteTest;

// Shared test setup for a TcpConnectionService that reads from a given stream. Tests
// written before 2026-11-11 keep their own copies of this setup unchanged.
internal static class TestConnections
{
    private static readonly FieldInfo StreamField =
        typeof(TcpConnectionService).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(TcpConnectionService), "_stream");

    /// <summary>A connection that reads <paramref name="stream"/>; a limit of 0 means
    /// the default maximum frame size.</summary>
    internal static TcpConnectionService Over(Stream stream, int maxFrameSize = 0)
    {
        var connection = new TcpConnectionService(() => false, (_, _, _, _) => true,
            (_, _) => Task.CompletedTask, (_, _) => { }, true, maxFrameSize: maxFrameSize);
        StreamField.SetValue(connection, stream);
        return connection;
    }
}

// Reads a frame with a raw message state code and retained total, for the tests that
// hold them (ParserRetentionTests, PayloadParsingTests). It is in the test assembly, so
// production code has no entry point that takes a raw state. Like the production read,
// it reports every failure through the returned task: a code outside the receive
// states, or a total above the connection's limit, faults the task before any byte is
// read, because the core's Admit requires both and compiled Dafny does not check them.
internal static class DataframeParsingRawState
{
    internal static async Task<Dataframe?> ReadDataframeAsync(
        this TcpConnectionService tcpConnection,
        CancellationToken ct, byte state = 0, uint total = 0)
    {
        var messageState = ReceiveState.FromCode(state);
        if (total > (uint)tcpConnection.MaxFrameSize)
            throw new ArgumentOutOfRangeException(nameof(total), total, "The retained total exceeds the connection's limit.");
        return await tcpConnection.ReadDataframeAsync(messageState, total, ct).ConfigureAwait(false);
    }
}
