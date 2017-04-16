using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebsocketClientLite.PCL.Model.Base
{
    internal abstract class HttpHeaderBase : ParseControlBase
    {
        public IDictionary<string, string> Headers { get; internal set; } = new Dictionary<string, string>();
    }
}
