using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Reactive.Disposables;
using IWebsocketClientLite;
using WebsocketClientLite.Helper;
using WebsocketClientLite.Model;
using static WebsocketClientLite.Helper.DataframeParsing;


namespace WebsocketClientLite.Service;

internal class WebsocketParserHandler : IDisposable
{
    private readonly TcpConnectionService _tcpConnectionService;

    internal IEnumerable<string>? SubprotocolAcceptedNames { get; private set; }

    internal WebsocketParserHandler(
        TcpConnectionService tcpConnectionService)
    {
        _tcpConnectionService = tcpConnectionService;
    }

    internal IObservable<Dataframe?> DataframeObservable() => 
        Observable.Create<Dataframe?>(async obs =>
        {
            var cts = new CancellationTokenSource();

            var dataframe = await _tcpConnectionService.CreateDataframe(cts.Token)
                .PayloadBitLenght()
                .PayloadLenght()
                .GetPayload(cts.Token)
                .ConfigureAwait(false);

            if (dataframe is not null)
            {
                while (!dataframe.FIN)
                {
                    // Merge fragments efficiently
                    dataframe = await GetNextDataframe(dataframe).ConfigureAwait(false);

                    if (dataframe is null) break;                      
                }

                obs.OnNext(dataframe);
            }
                         
            obs.OnCompleted();

            return Disposable.Create(() => cts?.Cancel());

            async Task<Dataframe?> GetNextDataframe(Dataframe? df)
            {
                var nextDataframe = await GetDataframe().ConfigureAwait(false);

                if (nextDataframe is not null 
                    && nextDataframe.DataStream is not null
                    && df is not null
                    && df.DataStream is not null)
                {
#if NETSTANDARD2_0
                    byte[]? rented = null;
                    try
                    {
                        rented = ArrayPool<byte>.Shared.Rent(81920);
                        int read;
                        nextDataframe.DataStream.Position = 0;
                        while ((read = nextDataframe.DataStream.Read(rented, 0, rented.Length)) > 0)
                        {
                            await df.DataStream.WriteAsync(rented, 0, read, cts.Token);
                        }
                    }
                    finally
                    {
                        if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
                    }
#else
                    await nextDataframe.DataStream.CopyToAsync(df.DataStream).ConfigureAwait(false);
#endif
                    df = df with { FIN = nextDataframe.FIN };
                }

                return df;
            }

            async Task<Dataframe?> GetDataframe()
            {
                var newDataframe = await _tcpConnectionService.CreateDataframe(cts.Token)
                    .PayloadBitLenght()
                    .PayloadLenght()
                    .GetPayload(cts.Token)
                    .ConfigureAwait(false);

                if (newDataframe is null)
                {
                    return null;
                }

                if (newDataframe.Opcode
                    is OpcodeKind.Text
                    or OpcodeKind.Binary
                    or OpcodeKind.Continuation
                    || newDataframe.Fragment is FragmentKind.Last)
                {
                    return newDataframe;
                }
                else
                {
                    obs.OnNext(newDataframe);
                }

                return null;
            }
        });        
    
    public void Dispose()
    {

    }
}
