using HttpMachine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Threading.Tasks;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;
using System.Linq;
using System.Threading;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketParserHandler : IDisposable
    {
        private readonly TcpConnectionService _tcpConnectionService;
        private readonly TextDataParser _textDataParser;

        private bool ExcludeZeroApplicationDataInPong { get; }
        internal IEnumerable<string> SubprotocolAcceptedNames { get; private set; }

        internal WebsocketParserHandler(
            TcpConnectionService tcpConnectionService,
            bool excludeZeroApplicationDataInPong,
            ControlFrameHandler controlFrameHandler)
        {
            _tcpConnectionService = tcpConnectionService;
            _textDataParser = new TextDataParser(controlFrameHandler);
            ExcludeZeroApplicationDataInPong = excludeZeroApplicationDataInPong;
        }
 
        internal IObservable<(HandshakeStateKind dataReceiveState, string message)> CreateWebsocketListenerObservable(
            Stream stream)
        {
            using var parserDelegate = new HttpParserDelegate();
            using var parserHandler = new HttpCombinedParser(parserDelegate);

            return _tcpConnectionService.ByteStreamObservable()
                .Select(@byte => Observable.FromAsync(ct => ParseText(@byte, stream,ct)))                            
                .Concat()
                .DistinctUntilChanged(tuple => tuple.dataReceiveState)
                .Where(tuple => tuple.dataReceiveState == HandshakeStateKind.MessageReceived);
        }

        private async Task<(HandshakeStateKind dataReceiveState, string message)> ParseText(
            byte[] bytes, 
            Stream stream,
            CancellationToken ct)
        {
            await _textDataParser.ParseText(
                stream,
                bytes[0],
                ct,
                ExcludeZeroApplicationDataInPong);

            if (_textDataParser.IsCloseReceived)
            {
                return (HandshakeStateKind.Exiting, null);
            }

            if (_textDataParser.HasNewMessage)
            {
                return (HandshakeStateKind.MessageReceived, _textDataParser.NewMessage);
            }

            return (HandshakeStateKind.IsParsing, null);
        }

        public void Dispose()
        {
            _textDataParser?.Dispose();
        }
    }
}
