using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IWebsocketClientLite.PCL
{
    public enum ConnectionStatus
    {
        Connecting,
        Connected,
        Disconnected,
        Disconnecting,
        Aborted,
        Sending,
        DeliveryAcknowledged,
        MultiFrameSendingBegin,
        MultiFrameSendingContinue,
        FrameDeliveryAcknowledged,
        MultiFrameSendingLast,
        ConnectionFailed
    }
}
