using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebsocketClientLite.PCL.Model
{
    internal enum DataReceiveState
    {
        Start,
        IsListeningForHandShake,
        IsListening,
        Exiting
    }
}
