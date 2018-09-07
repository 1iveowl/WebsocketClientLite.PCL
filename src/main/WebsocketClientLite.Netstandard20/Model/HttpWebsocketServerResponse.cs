using WebsocketClientLite.PCL.Model.Base;

namespace WebsocketClientLite.PCL.Model
{
    internal class HttpWebsocketServerResponse : HttpHeaderBase
    {
        public int MajorVersion { get; internal set; }
        public int MinorVersion { get; internal set; }
        public int StatusCode { get; internal set; }
        public string ResponseReason { get; internal set; }
    }
}
