namespace WebsocketClientLite.PCL.CustomException
{
    public class WebsocketClientLiteTcpConnectException : System.Exception
    {
        public WebsocketClientLiteTcpConnectException() : base() { }

        public WebsocketClientLiteTcpConnectException(string message) : base(message) { }

        public WebsocketClientLiteTcpConnectException(string message, System.Exception inner) : base(message, inner) { }
    }
}
