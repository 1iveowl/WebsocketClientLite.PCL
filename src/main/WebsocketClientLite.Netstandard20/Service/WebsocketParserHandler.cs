using HttpMachine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;
using System.Linq;
using System.Threading;
using System.Reactive.Concurrency;
using WebsocketClientLite.PCL.Helper;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketParserHandler : IDisposable
    {
        //private readonly IObserver<DataReceiveState> _observerDataReceiveMode;
        private readonly TcpConnectionService _tcpConnectionService;
        private readonly TextDataParser _textDataParser;
        private readonly Func<Stream, byte[], CancellationToken, Task<int>> _readOneByteFunc;
        private readonly Action<ConnectionStatus> _connectionStatusAction;

        //private DataReceiveState _dataReceiveState;
        private bool ExcludeZeroApplicationDataInPong { get; }
        private bool IsSubprotocolAccepted { get; set; }

        //internal readonly IObservable<DataReceiveState> DataReceiveStateObservable;
        internal IEnumerable<string> SubprotocolAcceptedNames { get; private set; }
        //internal HttpWebSocketParserDelegate ParserDelegate { get; private set; }

        internal WebsocketParserHandler(
            TcpConnectionService tcpConnectionService,
            bool subprotocolAccepted,
            bool excludeZeroApplicationDataInPong,
            //HttpWebSocketParserDelegate httpWebSocketParserDelegate,
            Func<Stream, byte[], CancellationToken, Task<int>> readOneByteFunc,
            ControlFrameHandler controlFrameHandler,
            Action<ConnectionStatus> connectionStatusAction)
        {
            _tcpConnectionService = tcpConnectionService;
            _textDataParser = new TextDataParser(controlFrameHandler);

            _connectionStatusAction = connectionStatusAction;
            _readOneByteFunc = readOneByteFunc;

            //_observerDataReceiveMode = dataReceiveSubject.AsObserver();

            //ParserDelegate = httpWebSocketParserDelegate;

            IsSubprotocolAccepted = subprotocolAccepted;
            ExcludeZeroApplicationDataInPong = excludeZeroApplicationDataInPong;

            //DataReceiveStateObservable = dataReceiveSubject.AsObservable(); 
        }
        //internal IObservable<string> CreateWebsocketListenerObservable(
        //    Stream stream,
        //    IEnumerable<string> subProtocols = null,
        //    IScheduler scheduler = default,
        //    IObservable<Unit> _clientPingObservable = default,
        //    bool isListeningForHandshake = false) =>
        //        Observable.Create<string>(obs =>
        //        {   
        //            using var parserHandler = new HttpCombinedParser(ParserDelegate);

        //            _textDataParser.Reinitialize();

        //            IObservable<DataReceiveState> dataReceiveStateObservable;

        //            if (isListeningForHandshake)
        //            {
        //                _connectionStatusAction(ConnectionStatus.ConnectingToTcpSocket);
        //                dataReceiveStateObservable = Observable.Return(DataReceiveState.IsListeningForHandShake);
        //                //_dataReceiveState = DataReceiveState.IsListeningForHandShake;
        //                //_observerDataReceiveMode.OnNext(_dataReceiveState);
        //            }
        //            else
        //            {
        //                dataReceiveStateObservable = Observable.Return(DataReceiveState.IsListening);
        //            }

        //            //var disposableReceiveState = DataReceiveStateObservable.Subscribe(s =>
        //            //    {
        //            //        _dataReceiveState = s;
        //            //    },
        //            //    obs.OnError,
        //            //    () =>
        //            //    {
        //            //        Debug.WriteLine("DataReceiveObservable completed");
        //            //    });

        //            //var disposableStreamListener =
        //            //    Observable.While(
        //            //        () => _dataReceiveState == DataReceiveState.IsListeningForHandShake
        //            //            || _dataReceiveState == DataReceiveState.IsListening,
        //            //        Observable.FromAsync(ct => ReadOneByte(stream, ct)))
        //            //    .Select(bytes => ParseObservable(stream, bytes))
        //            //    .Concat()
        //            //    .Subscribe(
        //            //    _ =>
        //            //    {
        //            //        if (_textDataParser.IsCloseReceived)
        //            //        {
        //            //            _observerDataReceiveMode.OnNext(DataReceiveState.Exiting);
        //            //            _observerDataReceiveMode.OnCompleted();
        //            //        }
        //            //    },
        //            //    obs.OnError,
        //            //    obs.OnCompleted);


        //            //var disposableStreamListener = dataReceiveStateObservable
        //            //    .CombineLatest(Observable.FromAsync(ct => ReadOneByte(stream, ct))
        //            //    .SelectMany(x => x)
        //            //    );
        //            //.Select(bytes => ParseObservable(stream, bytes))
        //            //.Concat());


        //            var disposableStreamListener = Observable.FromAsync(ct => ReadOneByte(stream, ct))
        //                .CombineLatest(dataReceiveStateObservable)
        //                .Select(tuple => ParseObservable(stream, tuple.First, tuple.Second));



        //            if (_clientPingObservable is not null)
        //            {
        //                var clientPingDisposable = _clientPingObservable.Subscribe();

        //                return new CompositeDisposable(
        //                    clientPingDisposable,
        //                    //disposableReceiveState,
        //                    disposableStreamListener);
        //            }

        //            return new CompositeDisposable(
        //                //disposableReceiveState, 
        //                disposableStreamListener);

        //            IObservable<(DataReceiveState dataReceiveState, string message)> ParseObservable(
        //                Stream stream, 
        //                byte[] @byte, 
        //                DataReceiveState dataReceiveState) => 
        //                    scheduler is null 
        //                        ? Observable.FromAsync(ct => Parse(
        //                                @byte,
        //                                stream,
        //                                ParserDelegate,
        //                                parserHandler,
        //                                subProtocols,
        //                                dataReceiveState,
        //                                ct))
        //                        : Observable.FromAsync(ct => Parse(
        //                            @byte,
        //                            stream,
        //                            ParserDelegate,
        //                            parserHandler,
        //                            subProtocols,
        //                            dataReceiveState,
        //                            ct))
        //                            .ObserveOn(scheduler);

        //        })
        //    .Publish().RefCount();

        internal IObservable<(DataReceiveState dataReceiveState, string message)> CreateWebsocketListenerObservable(
            Stream stream,
            IEnumerable<string> subProtocols = null,
            IScheduler scheduler = default,
            //IObservable<Unit> clientPingObservable = default,            
            bool isListeningForHandshake = false)
        {
            var parserDelegate = new HttpParserDelegate();

            using var parserHandler = new HttpCombinedParser(parserDelegate);

            //IObservable<DataReceiveState> dataReceiveStateObservable;

            //if (isListeningForHandshake)
            //{
            //    _connectionStatusAction(ConnectionStatus.ConnectingToTcpSocket);
            //    dataReceiveStateObservable = Observable.Return(DataReceiveState.IsListeningForHandShake);
            //    //_dataReceiveState = DataReceiveState.IsListeningForHandShake;
            //    //_observerDataReceiveMode.OnNext(_dataReceiveState);
            //}
            //else
            //{
            //    dataReceiveStateObservable = Observable.Return(DataReceiveState.IsListening);
            //}

            //DataReceiveState dataReceiveState = isListeningForHandshake
            //    ? DataReceiveState.IsListeningForHandShake
            //    : DataReceiveState.IsListening;

            return _tcpConnectionService.ByteStreamObservable()
                .Select(@byte => ParseObservable(stream, @byte, parserDelegate))
                .Concat()
                .DistinctUntilChanged(tuple => tuple.dataReceiveState)
                .Where(tuple => tuple.dataReceiveState == DataReceiveState.MessageReceived);
                //.TakeWhile(tuple => tuple.dataReceiveState == DataReceiveState.IsListeningForHandShake);


            //return Observable.Return(dataReceiveState)
            //    .Select(d =>
            //        Observable.While(
            //            () => true,
            //            Observable.FromAsync(ct => _tcpConnectionService.ReadOneByteFromStream(ct))
            //                //.CombineLatest(Observable.Return(dataReceiveState))
            //                .Select(@byte => ParseObservable(stream, @byte, d))
            //                .Concat()))
            //    .Concat()
            //    .Do(tuple => dataReceiveState = tuple.dataReceiveState)
            //    .Distinct(x => x.message);


            //var t = Observable.FromAsync(ct => ReadOneByte(stream, ct))
            //    .CombineLatest(Observable.Return(dataReceiveState))
            //    .Select(tuple => ParseObservable(stream, tuple.First, tuple.Second))
            //    .Concat()
            //    .Do(tuple => dataReceiveState = tuple.dataReceiveState);

            //var u = Observable.While(() => true, t);

            //return Observable.FromAsync(ct => ReadOneByte(stream, ct))
            //    .CombineLatest(dataReceiveStateObservable)
            //    .Select(tuple => ParseObservable(stream, tuple.First, tuple.Second))
            //    .Concat()          
            //    .Publish().RefCount();

            //if (_clientPingObservable is not null)
            //{
            //    var clientPingDisposable = _clientPingObservable.Subscribe();

            //    return new CompositeDisposable(
            //        clientPingDisposable,
            //        //disposableReceiveState,
            //        disposableStreamListener);
            //}

            //return new CompositeDisposable(
            //    //disposableReceiveState, 
            //    disposableStreamListener);

            IObservable<(DataReceiveState dataReceiveState, string message)> ParseObservable(
                Stream stream,
                byte[] @byte,
                HttpParserDelegate parserDelegate) =>
                    scheduler is null
                        ? Observable.FromAsync(ct => Parse(
                                @byte,
                                stream,
                                parserDelegate,
                                parserHandler,
                                subProtocols,
                                ct))
                        : Observable.FromAsync(ct => Parse(
                            @byte,
                            stream,
                            parserDelegate,
                            parserHandler,
                            subProtocols,
                            ct))
                            .ObserveOn(scheduler);
        }


        //internal IObservable<(DataReceiveState dataReceiveState, string message)> CreateWebsocketListenerObservable(
        //    Stream stream,
        //    Func<CancellationToken, Task> sendHandShakeFunc,
        //    Func<CancellationToken, Task> waitForHandshake,
        //    IEnumerable<string> subProtocols = null,
        //    IScheduler scheduler = default,
        //    IObservable<Unit> _clientPingObservable = default,
        //    bool isListeningForHandshake = false)
        //{
        //    return Observable.Create<(DataReceiveState dataReceiveState, string message)>(obs =>
        //    {
        //        using var parserHandler = new HttpCombinedParser(ParserDelegate);

        //        var handshakeParser = new HandshakeParser(
        //            parserHandler,
        //            _connectionStatusAction);

        //        _textDataParser.Reinitialize();

        //        DataReceiveState dataReceiveState = DataReceiveState.IsListeningForHandShake;
        //            //? DataReceiveState.IsListeningForHandShake
        //            //: DataReceiveState.IsListening;



        //        //var disposable = Observable.Return(dataReceiveState)
        //        ////.CombineLatest(Observable.FromAsync(c => sendHandShakeFunc(c)))

        //        //.SelectMany(state =>
        //        //    Observable.While(
        //        //        () => state == DataReceiveState.IsListeningForHandShake,
        //        //        Observable.FromAsync(ct => ReadOneByte(stream, ct))
        //        //            .Select(@byte => ParseObservable(stream, @byte, state)).Concat())
        //        //    .Distinct(tuple => tuple.dataReceiveState)
        //        //    .Do(tuple => dataReceiveState = tuple.dataReceiveState)
        //        //    )
        //        //.Subscribe(x => obs.OnNext(x));

        //        return disposable;

        //        IObservable<(DataReceiveState dataReceiveState, string message)> ParseObservable(
        //            Stream stream,
        //            byte[] @byte,
        //            DataReceiveState dataReceiveState) =>
        //                scheduler is null
        //                    ? Observable.FromAsync(ct => Parse(
        //                            @byte,
        //                            stream,
        //                            ParserDelegate,
        //                            parserHandler,
        //                            subProtocols,
        //                            dataReceiveState,
        //                            ct))
        //                    : Observable.FromAsync(ct => Parse(
        //                        @byte,
        //                        stream,
        //                        ParserDelegate,
        //                        parserHandler,
        //                        subProtocols,
        //                        dataReceiveState,
        //                        ct))
        //                        .ObserveOn(scheduler);

        //    });


        //}

        private async Task<(DataReceiveState dataReceiveState, string message)> Parse(
            byte[] bytes, 
            Stream stream,
            HttpParserDelegate parserDelegate, 
            HttpCombinedParser parserHandler,
            IEnumerable<string> subProtocols,
            CancellationToken ct)
        {
            await _textDataParser.Parse(
                stream,
                bytes[0],
                ct,
                ExcludeZeroApplicationDataInPong);

            if (_textDataParser.IsCloseReceived)
            {
                return (DataReceiveState.Exiting, null);
            }

            if (_textDataParser.HasNewMessage)
            {
                return (DataReceiveState.MessageReceived, _textDataParser.NewMessage); ;
                //obs.OnNext(_textDataParser.NewMessage);
            }

            return (DataReceiveState.IsParsing, null);
            //switch (dataReceiveState)
            //{
            //case DataReceiveState.IsListeningForHandShake:

            //    parserHandler.Execute(bytes);

            //    return (
            //        HandshakeParser(parserDelegate, subProtocols, dataReceiveState), 
            //        null);

            //    case DataReceiveState.IsListening:

            //        await _textDataParser
            //            .Parse(
            //                stream, 
            //                bytes[0], 
            //                ct,
            //                ExcludeZeroApplicationDataInPong);

            //        if (_textDataParser.IsCloseReceived)
            //        {
            //            return (DataReceiveState.Exiting, null);
            //        }

            //        if (_textDataParser.HasNewMessage)
            //        {
            //            return (dataReceiveState, _textDataParser.NewMessage);
            //            //obs.OnNext(_textDataParser.NewMessage);
            //        }
            //        break;
            //}

            //return (DataReceiveState.Exiting, null);
        }

        //private DataReceiveState HandshakeParser(
        //    HttpWebSocketParserDelegate parserDelegate, 
        //    IEnumerable<string> subProtocols,
        //    DataReceiveState dataReceiveState)
        //{
        //    if (parserDelegate.HttpRequestResponse is not null 
        //        && parserDelegate.HttpRequestResponse.IsEndOfMessage)
        //    {
        //        if (parserDelegate.HttpRequestResponse.StatusCode == 101)
        //        {
        //            if (subProtocols is not null)
        //            {
        //                IsSubprotocolAccepted =
        //                    parserDelegate?.HttpRequestResponse?.Headers?.ContainsKey(
        //                        "SEC-WEBSOCKET-PROTOCOL") ?? false;

        //                if (IsSubprotocolAccepted)
        //                {
        //                    SubprotocolAcceptedNames =
        //                        parserDelegate?.HttpRequestResponse?.Headers?["SEC-WEBSOCKET-PROTOCOL"];

        //                    if (!SubprotocolAcceptedNames?.Any(sp => sp.Length > 0) ?? false)
        //                    {
        //                        _connectionStatusAction(ConnectionStatus.Aborted);
        //                        throw new WebsocketClientLiteException("Server responded with blank Sub Protocol name");
        //                    }
        //                }
        //                else
        //                {
        //                    _connectionStatusAction(ConnectionStatus.Aborted);
        //                    throw new WebsocketClientLiteException("Server did not support any of the needed Sub Protocols");
        //                }
        //            }

        //            //_dataReceiveState = DataReceiveState.IsListening;
        //            _connectionStatusAction(ConnectionStatus.WebsocketConnected);

        //            Debug.WriteLine("HandShake completed");
        //            //_observerDataReceiveMode.OnNext(DataReceiveState.IsListening);

        //            return DataReceiveState.IsListening;
        //        }
        //        else
        //        {
        //            throw new WebsocketClientLiteException($"Unable to connect to websocket Server. " +
        //                                $"Error code: {parserDelegate.HttpRequestResponse.StatusCode}, " +
        //                                $"Error reason: {parserDelegate.HttpRequestResponse.ResponseReason}");
        //        }
        //    }

        //    return dataReceiveState;
        //}

        //private void StopReceivingData()
        //{
        //    _observerDataReceiveMode.OnNext(DataReceiveState.Exiting);
        //    _observerDataReceiveMode.OnCompleted();
        //}

        //private async Task<byte[]> ReadOneByte(
        //    Stream stream,
        //    CancellationToken ct)
        //{
        //    var oneByte = new byte[1];

        //    try
        //    {
        //        if (stream == null)
        //        {
        //            throw new WebsocketClientLiteException("Read stream cannot be null.");
        //        }

        //        if (!stream.CanRead)
        //        {
        //            throw new WebsocketClientLiteException("Websocket connection have been closed.");
        //        }

        //        var length = await _readOneByteFunc(stream, oneByte, ct);

        //        //var bytesRead = await stream.ReadAsync(oneByte, 0, 1);

        //        if (length == 0)
        //        {
        //            throw new WebsocketClientLiteException("Websocket connection aborted unexpectedly. Check connection and socket security version/TLS version).");
        //        }
        //    }
        //    catch (ObjectDisposedException)
        //    {
        //        Debug.WriteLine("Ignoring Object Disposed Exception - This is an expected exception");
        //    }
        //    return oneByte;
        //}

        public void Dispose()
        {
            _textDataParser?.Dispose();
            //ParserDelegate?.Dispose();
        }
    }
}
