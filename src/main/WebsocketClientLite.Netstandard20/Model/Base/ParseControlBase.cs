using System;
using System.Reactive.Subjects;
using HttpMachine;
using WebsocketClientLite.PCL.Parser;

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

        protected ParseControlBase()
        {
        }
    }
}
