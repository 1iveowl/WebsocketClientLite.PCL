using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Reactive.Disposables;
using IWebsocketClientLite;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Model;
using static WebsocketClientLite.Helper.DataframeParsing;


namespace WebsocketClientLite.Service;

internal class WebsocketParserHandler : IDisposable
{
    private readonly TcpConnectionService _tcpConnectionService;

    internal WebsocketParserHandler(
        TcpConnectionService tcpConnectionService)
    {
        _tcpConnectionService = tcpConnectionService;
    }

    internal IObservable<Dataframe?> DataframeObservable() =>
        Observable.Create<Dataframe?>(async obs =>
        {
            var cts = new CancellationTokenSource();

            try
            {
                var dataframe = await _tcpConnectionService.ReadDataframeAsync(cts.Token).ConfigureAwait(false);

                if (dataframe is not null)
                {
                    if (!dataframe.FIN)
                    {
                        dataframe = await Reassemble(dataframe).ConfigureAwait(false);
                    }

                    if (dataframe is not null)
                    {
                        obs.OnNext(dataframe);
                    }
                }

                obs.OnCompleted();
            }
            catch (Exception ex)
            {
                obs.OnError(ex);
            }

            return Disposable.Create(() =>
            {
                cts.Cancel();
                cts.Dispose();
            });

            // Reassembles a fragmented message by collecting payload segments and
            // concatenating them once. Control frames received between fragments
            // are emitted directly and do not interrupt reassembly (RFC 6455 §5.4).
            async Task<Dataframe?> Reassemble(Dataframe first)
            {
                var segments = new List<byte[]>();
                long total = 0;

                if (first.Payload is { Length: > 0 })
                {
                    segments.Add(first.Payload);
                    total = first.Payload.Length;
                }

                var current = first;
                while (!current.FIN)
                {
                    var next = await _tcpConnectionService.ReadDataframeAsync(cts.Token).ConfigureAwait(false);
                    if (next is null)
                    {
                        // Stream closed before the message completed.
                        return null;
                    }

                    if (next.Opcode is OpcodeKind.Continuation or OpcodeKind.Text or OpcodeKind.Binary)
                    {
                        if (next.Payload is { Length: > 0 })
                        {
                            total += next.Payload.Length;
                            if (total > _tcpConnectionService.MaxFrameSize)
                            {
                                throw new WebsocketClientLiteException(
                                    $"Reassembled message size ({total} bytes) exceeds the configured maximum of {_tcpConnectionService.MaxFrameSize} bytes.");
                            }

                            segments.Add(next.Payload);
                        }

                        current = next;
                    }
                    else
                    {
                        // Interleaved control frame: hand it downstream and keep reassembling.
                        obs.OnNext(next);
                    }
                }

                var merged = new byte[(int)total];
                int offset = 0;
                foreach (var segment in segments)
                {
                    Buffer.BlockCopy(segment, 0, merged, offset, segment.Length);
                    offset += segment.Length;
                }

                return first with { Payload = merged, FIN = true };
            }
        });

    public void Dispose()
    {
        // No instance-level resources to release here: the per-subscription
        // CancellationTokenSource is disposed when its subscription ends, and the
        // TcpConnectionService is owned and disposed by WebsocketService.
    }
}
