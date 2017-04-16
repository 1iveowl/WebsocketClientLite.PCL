using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HttpMachine;
using ISocketLite.PCL.Interface;

namespace WebsocketClientLite.PCL.Model.Base
{
    internal abstract class ParseControlBase : IParseControl
    {
        public MessageType MessageType { get; internal set; }
        public bool IsEndOfMessage { get; internal set; }

        public bool IsRequestTimedOut { get; internal set; } = false;

        public bool IsUnableToParseHttp { get; internal set; } = false;

        public string RemoteAddress { get; internal set; }

        public int RemotePort { get; internal set; }

        protected ParseControlBase() { }
    }
}
