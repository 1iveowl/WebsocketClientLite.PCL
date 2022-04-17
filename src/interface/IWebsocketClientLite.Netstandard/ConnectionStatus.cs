namespace IWebsocketClientLite.PCL
{
    public enum ConnectionStatus
    {
        Initialized =                               1000,
        ConnectingToTcpSocket =                     1010,
        TcpSocketConnected =                        1020,         
        ConnectingToSocketStream =                  1030,
        SocketStreamConnected =                     1040,
        SecureSocketStreamConnected =               1050,
        SendingHandshakeToWebsocketServer =         1060,
        HandshakeCompletedSuccessfully =            1070,
        WebsocketConnected =                        1080,
        Disconnected =                              1090,
        ForcefullyDisconnected =                    1100,
        ConnectionFailed =                          1110,

        Close =                                     2000,
        Text =                                      2010,
        Binary =                                    2020,
        Continuation =                              2030,

        DataframeReceived =                         3000,
        Aborted =                                   3010,
        SendComplete =                              3020,
        SendError =                                 3030,
        SingleFrameSending =                        3040,
        MultiFrameSendingContinue =                 3050,
        MultiFrameSendingFirst =                    3060,
        MultiFrameSendingLast =                     3070,

        PingSend =                                  4010,
        PingReceived =                              4020,
        PongSend =                                  4030,
        PongReceived =                              4040
    }
}
