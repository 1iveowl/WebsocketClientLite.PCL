using HttpMachine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;
using System.Linq;
using System.Threading;
using System.Reactive.Disposables;
using static WebsocketClientLite.PCL.Helper.DatagramParsing;
using WebsocketClientLite.PCL.Helper;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketParserHandler : IDisposable
    {
        private readonly TcpConnectionService _tcpConnectionService;
        private readonly WebsocketDatagramParserHandler _websocketDatagramParserHandler;

        private bool ExcludeZeroApplicationDataInPong { get; }
        internal IEnumerable<string> SubprotocolAcceptedNames { get; private set; }

        internal WebsocketParserHandler(
            TcpConnectionService tcpConnectionService,
            bool excludeZeroApplicationDataInPong,
            ControlFrameHandler controlFrameHandler)
        {
            _tcpConnectionService = tcpConnectionService;
            _websocketDatagramParserHandler = new WebsocketDatagramParserHandler(controlFrameHandler);
            ExcludeZeroApplicationDataInPong = excludeZeroApplicationDataInPong;
        }

        internal IObservable<Datagram> DatagramObservable() => 
            Observable.Create<Datagram>(async obs =>
            {
                var cts = new CancellationTokenSource();

                var datagram = await CreateDatagram(_tcpConnectionService, cts.Token)
                    .PayloadBitLenght()
                    .PayloadLenght()
                    .GetPayload();

                if (datagram is not null)
                {
                    while (!datagram.FIN)
                    {
                        // Merge fragments into one datagram.
                        datagram = await GetNextDataGram(datagram);
                    }

                    obs.OnNext(datagram);
                }
                             
                obs.OnCompleted();

                return Disposable.Create(() => cts.Cancel());

                async Task<Datagram> GetNextDataGram(Datagram datagram)
                {
                    var nextDatagram = await GetDataGram();

                    if (nextDatagram is not null)
                    {
                        await nextDatagram.DataStream.CopyToAsync(datagram.DataStream, cts.Token);

                        datagram = datagram with
                        {
                            FIN = nextDatagram.FIN,
                        };
                    }

                    return datagram;
                }

                async Task<Datagram> GetDataGram()
                {
                    var newDatagram = await CreateDatagram(_tcpConnectionService, cts.Token)
                        .PayloadBitLenght()
                        .PayloadLenght()
                        .GetPayload();

                    if (newDatagram.Opcode
                        is OpcodeKind.Text
                        or OpcodeKind.Binary
                        or OpcodeKind.Continuation
                        || newDatagram.Fragment is FragmentKind.Last)
                    {
                        return newDatagram;
                    }
                    else
                    {
                        obs.OnNext(newDatagram);
                    }

                    return null;
                }
            });
        
        // TODO Create observable that holdes ParserStateKind//using var parserDelegate = new HttpParserDelegate();//using var parserHandler = new HttpCombinedParser(parserDelegate);//var datagram = new WebsocketDatagram//{ //    Kind = WebsocketDatagramKind.Unknown, //    WebsocketParserState = WebsocketParserStateKind.Start//};//return Observable.FromAsync(ct => _tcpConnectionService.ReadOneByteArrayFromStream(ct))//    .Select(bytes => bytes[0])//    //_tcpConnectionService.ByteStreamObservable()//    //.Take(1)//    .Select(@byte => _websocketDatagramParserHandler.CreateDatagram(@byte));//.Where(d => d.WebsocketParserState == WebsocketParserStateKind.FirstByte)//.CombineLatest(Observable.FromAsync(ct => _tcpConnectionService.ReadOneByteArrayFromStream(ct)))//.Select(tuple => tuple.First);//.CombineLatest(_tcpConnectionService.ByteStreamObservable().Take(1))//.Select(tuple => _websocketDatagramParserHandler.GetPayloadLength(tuple.First, tuple.Second));//.Merge()//.Select(@byte => Observable.FromAsync(ct => ParseWebsocketDatagram(@byte, stream, datagram, ct)))//.Concat()//.Do(d => datagram = d)//.DistinctUntilChanged()//.Where(dataGram => dataGram.WebsocketParserState == WebsocketParserStateKind.HasReadPayload);//.Select(datagram => datagram with { WebsocketParserState = WebsocketParserStateKind.HasReadPayload})//.Select//.Where(tuple => tuple.dataReceiveState == HandshakeStateKind.MessageReceived)//.Do(_ => datagram = new WebsocketDatagram//{//    Kind = WebsocketDatagramKind.Unknown,//    WebsocketParserState = WebsocketParserStateKind.Start//});//return t;

        //private async Task<WebsocketDatagram> ParseWebsocketDatagram(
        //    byte @byte, 
        //    Stream stream,
        //    WebsocketDatagram datagram,
        //    CancellationToken ct)
        //{
        //    return await _websocketDatagramParserHandler.Parse(
        //        stream,
        //        @byte,
        //        datagram,
        //        ct,
        //        ExcludeZeroApplicationDataInPong);

        //    //if (_websocketDatagramParserHandler.IsCloseReceived)
        //    //{
        //    //    return (HandshakeStateKind.Exiting, null);
        //    //}

        //    //if (_websocketDatagramParserHandler.HasNewMessage)
        //    //{
        //    //    return (HandshakeStateKind.MessageReceived, _websocketDatagramParserHandler.NewMessage);
        //    //}

        //    //return (HandshakeStateKind.IsParsing, null);
        //}

        public void Dispose()
        {
            //_websocketDatagramParserHandler?.Dispose();
        }
    }
}
