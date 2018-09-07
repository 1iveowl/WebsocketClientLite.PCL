using System;
using System.Collections.Generic;
using System.Text;

namespace WebsocketClientLite.PCL.Parser
{
    internal enum ParserState
    {
        Start,
        HandshakeCompletedSuccessfully,
        HandshakeFailed,
        HandshakeTimedOut,
    }
}
