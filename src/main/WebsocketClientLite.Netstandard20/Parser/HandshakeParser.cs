using HttpMachine;
using IWebsocketClientLite.PCL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;

namespace WebsocketClientLite.PCL.Parser
{
    internal class HandshakeParser
    {
        private readonly Action<ConnectionStatus> _connectionStatusAction;
        private readonly HttpCombinedParser _parserHandler;
        private readonly WebsocketHandshakeParserDelegate _parserDelegate;

        internal IEnumerable<string> SubprotocolAcceptedNames { get; private set; }

        public HandshakeParser(
            HttpCombinedParser parserHandler,
            WebsocketHandshakeParserDelegate parserDelegate,
            Action<ConnectionStatus> connectionStatusAction)
        {
            _parserDelegate = parserDelegate;
            _parserHandler = parserHandler;
            _connectionStatusAction = connectionStatusAction;
        }

        internal DataReceiveState Parse(
            byte[] @byte,
            IEnumerable<string> subProtocols)
        {
            _parserHandler.Execute(@byte);

            if (_parserDelegate.HttpRequestResponse is not null
                && _parserDelegate.HttpRequestResponse.IsEndOfMessage)
            {
                if (_parserDelegate.HttpRequestResponse.StatusCode == 101)
                {
                    if (subProtocols is not null 
                        && _parserDelegate?.HttpRequestResponse?.Headers is not null)
                    {
                        if (_parserDelegate
                            .HttpRequestResponse
                            .Headers
                            .TryGetValue("SEC-WEBSOCKET-PROTOCOL", out var subprotocolAcceptedNames))
                        {
                            SubprotocolAcceptedNames = subprotocolAcceptedNames.Where(spn => subProtocols.Contains(spn));

                            if (!SubprotocolAcceptedNames?.Any() ?? true)
                            {
                                _connectionStatusAction(ConnectionStatus.Aborted);
                                throw new WebsocketClientLiteException("Server responded only with subprotocols not known by client.");
                            }
                        }
                        else
                        {
                            _connectionStatusAction(ConnectionStatus.Aborted);
                            throw new WebsocketClientLiteException("Server responded with blank Sub Protocol name");
                        }

                        //if (_parserDelegate?.HttpRequestResponse?.Headers?.ContainsKey("SEC-WEBSOCKET-PROTOCOL") 
                        //    ?? false)
                        //{
                        //    SubprotocolAcceptedNames =
                        //        _parserDelegate?.HttpRequestResponse?.Headers?["SEC-WEBSOCKET-PROTOCOL"];

                        //    if (!SubprotocolAcceptedNames?.Any(sp => sp.Length > 0) ?? false)
                        //    {
                        //        _connectionStatusAction(ConnectionStatus.Aborted);
                        //        throw new WebsocketClientLiteException("Server responded with blank Sub Protocol name");
                        //    }
                        //}
                        //else
                        //{
                        //    _connectionStatusAction(ConnectionStatus.Aborted);
                        //    throw new WebsocketClientLiteException("Server did not support any of the needed Sub Protocols");
                        //}
                    }

                    //_dataReceiveState = DataReceiveState.IsListening;
                    _connectionStatusAction(ConnectionStatus.WebsocketConnected);

                    Debug.WriteLine("HandShake completed");
                    //_observerDataReceiveMode.OnNext(DataReceiveState.IsListening);

                    return DataReceiveState.IsListening;
                }
                else
                {
                    throw new WebsocketClientLiteException($"Unable to connect to websocket Server. " +
                                        $"Error code: {_parserDelegate.HttpRequestResponse.StatusCode}, " +
                                        $"Error reason: {_parserDelegate.HttpRequestResponse.ResponseReason}");
                }
            }
            return DataReceiveState.IsListeningForHandShake;
        }
    }
}
