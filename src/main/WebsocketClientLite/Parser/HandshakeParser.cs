﻿using HttpMachine;
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using WebsocketClientLite.CustomException;
using IWebsocketClientLite;

namespace WebsocketClientLite.Parser;

internal class HandshakeParser(
    HttpCombinedParser parserHandler,
    HandshakeParserDelegate parserDelegate,
    Action<ConnectionStatus, Exception> connectionStatusAction)
{
    private readonly Action<ConnectionStatus, Exception> _connectionStatusAction = connectionStatusAction;
    private readonly HttpCombinedParser _parserHandler = parserHandler;
    private readonly HandshakeParserDelegate _parserDelegate = parserDelegate;

    internal IEnumerable<string>? SubprotocolAcceptedNames { get; private set; }

    internal bool Parse(
        byte[]? byteArray,
        IEnumerable<string>? subProtocols)
    {
        if (byteArray is null)
        {
            return false;
        }

        _parserHandler.Execute(byteArray);

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
                            _connectionStatusAction(
                                ConnectionStatus.Aborted,
                                new WebsocketClientLiteException("Server responded only with subprotocols not known by client."));
                        }
                    }
                    else
                    {
                        _connectionStatusAction(
                            ConnectionStatus.Aborted,
                            new WebsocketClientLiteException("Server responded with blank Sub Protocol name")
                            );
                    }
                }

                Debug.WriteLine("HandShake completed");            
                return true;
            }
            else
            {
                _connectionStatusAction(
                    ConnectionStatus.Aborted,
                    new WebsocketClientLiteException($"Unable to connect to websocket Server. " +
                                    $"Error code: {_parserDelegate.HttpRequestResponse.StatusCode}, " +
                                    $"Error reason: {_parserDelegate.HttpRequestResponse.ResponseReason}"));
            }
        }

        return false;
    }
}
