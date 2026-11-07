using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using IWebsocketClientLite;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Helper;
using WebsocketClientLite.Model;
using Protocol = WebsocketClientLite.VerifiedProtocol.Protocol;

namespace WebsocketClientLite.Service;

internal class WebsocketParserHandler(TcpConnectionService tcpConnectionService) : IDisposable
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "All read-loop failures must be delivered through OnError.")]
    internal IObservable<Dataframe?> DataframeObservable() =>
        Observable.Create<Dataframe?>(async (obs, token) =>
        {
            byte state = 0;
            uint total = 0;
            Dataframe? first = null;
            var segments = new List<byte[]>();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var frame = await tcpConnectionService.ReadDataframeAsync(token, state, total).ConfigureAwait(false);
                    if (frame is null) break;
                    var payload = frame.Payload ?? Array.Empty<byte>();
                    Protocol.Step(state, (byte)frame.Opcode, frame.FIN, total, (uint)payload.Length,
                        (uint)tcpConnectionService.MaxFrameSize, out bool ok, out byte next,
                        out uint size, out bool emit);
                    if (!ok) throw new WebsocketClientLiteException("Protocol error: invalid fragmentation sequence or message exceeds the configured maximum.");

                    if ((byte)frame.Opcode >= 8)
                    {
                        state = next;
                        total = size;
                        obs.OnNext(frame);
                        if (state == 8) break;
                        continue;
                    }

                    if (state == 0) first = frame;
                    if (payload.Length > 0) segments.Add(payload);
                    state = next;
                    total = size;
                    if (!emit) continue;

                    byte[] merged;
                    if (segments.Count == 0) merged = Array.Empty<byte>();
                    else if (segments.Count == 1) merged = segments[0];
                    else
                    {
                        // Step's accumulated total equals the retained segment lengths.
                        // Merge proves exact concatenation and bounds for every copied byte.
                        merged = Protocol.Merge(segments.ToArray(), total);
                    }
                    var message = first! with { Payload = merged, FIN = true, Length = total, Fragment = FragmentKind.None };
                    if (message.Opcode == OpcodeKind.Text) ProtocolPayload.ValidateText(merged);
                    obs.OnNext(message);
                    segments.Clear();
                    first = null;
                    total = 0;
                }
                obs.OnCompleted();
            }
            catch (Exception ex) { obs.OnError(ex); }
        });

    public void Dispose() { }
}
