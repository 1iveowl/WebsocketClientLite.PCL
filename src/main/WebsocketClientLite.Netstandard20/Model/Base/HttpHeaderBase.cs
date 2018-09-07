using System.Collections.Generic;

namespace WebsocketClientLite.PCL.Model.Base
{
    internal abstract class HttpHeaderBase : ParseControlBase
    {
        public IDictionary<string, string> Headers { get; internal set; } = new Dictionary<string, string>();
    }
}
