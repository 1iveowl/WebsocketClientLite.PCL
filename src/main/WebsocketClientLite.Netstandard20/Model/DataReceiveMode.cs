﻿namespace WebsocketClientLite.PCL.Model
{
    internal enum DataReceiveState
    {
        Start,
        IsListeningForHandShake,
        IsListening,
        IsParsing,
        MessageReceived,
        Exiting
    }
}
