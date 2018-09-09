namespace WebsocketClientLite.PCL.Model.Base
{
    internal interface IParseControl
    {

        bool IsEndOfMessage { get; }

        bool IsRequestTimedOut { get; }

        bool IsUnableToParseHttp { get; }
    }
}
