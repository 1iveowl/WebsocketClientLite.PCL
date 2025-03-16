namespace IWebsocketClientLite;

public enum ConnectionStatus
{
    /// <summary>
    /// Start state.
    /// </summary>
    Initialized =                               1000,
    /// <summary>
    /// Connecting to TCP socket.
    /// </summary>
    ConnectingToTcpSocket =                     1010,
    /// <summary>
    /// TCP socket connected.
    /// </summary>
    TcpSocketConnected =                        1020,
    /// <summary>
    /// Connecting to socket stream.
    /// </summary>
    ConnectingToSocketStream =                  1030,
    /// <summary>
    /// Socket stream connected.
    /// </summary>
    SocketStreamConnected =                     1040,
    /// <summary>
    /// Secure socket stream connected.
    /// </summary>
    SecureSocketStreamConnected =               1050,
    /// <summary>
    /// Sending handshake to websocket server.
    /// </summary>
    SendingHandshakeToWebsocketServer =         1060,
    /// <summary>
    /// Handshake completed successfully.
    /// </summary>
    HandshakeCompletedSuccessfully =            1070,
    /// <summary>
    /// Websocket connected.
    /// </summary>
    WebsocketConnected =                        1080,
    /// <summary>
    /// Websocket disconnected.
    /// </summary>
    Disconnected =                              1090,
    /// <summary>
    /// Websocket forcefully disconnected.
    /// </summary>
    ForcefullyDisconnected =                    1100,
    /// <summary>
    /// Websocket connection failed.
    /// </summary>
    ConnectionFailed =                          1110,
    /// <summary>
    /// Web socket connection aborted. 
    /// </summary>
    Aborted =                                   1120,

    /// <summary>
    /// Close control frame.
    /// </summary>
    Close =                                     2000,
    /// <summary>
    /// Text control frame.
    /// </summary>
    Text =                                      2010,
    /// <summary>
    /// Binary control frame
    /// </summary>
    Binary =                                    2020,
    /// <summary>
    /// Continuation control frame
    /// </summary>
    Continuation =                              2030,

    /// <summary>
    /// Dataframe received.
    /// </summary>
    DataframeReceived =                         3000,

    /// <summary>
    /// Send completed.
    /// </summary>
    SendComplete =                              3010,
    /// <summary>
    /// Send error.
    /// </summary>
    SendError =                                 3020,
    /// <summary>
    /// Single frame sending.
    /// </summary>
    SingleFrameSending =                        3030,
    /// <summary>
    /// Multiframe continue sending.
    /// </summary>
    MultiFrameSendingContinue =                 3040,
    /// <summary>
    /// Multiframe first sending.
    /// </summary>
    MultiFrameSendingFirst =                    3050,
    /// <summary>
    /// Multiframe last sending.
    /// </summary>
    MultiFrameSendingLast =                     3060,

    /// <summary>
    /// Ping send.
    /// </summary>
    PingSend =                                  4010,
    /// <summary>
    /// Ping received.
    /// </summary>
    PingReceived =                              4020,
    /// <summary>
    /// Ping send.
    /// </summary>
    PongSend =                                  4030,
    /// <summary>
    /// Pong received.
    /// </summary>
    PongReceived =                              4040
}
