namespace WebsocketClientLite.CustomException;

public class WebsocketClientLiteException : System.Exception
{
    public WebsocketClientLiteException()
    {
        
    }

    public WebsocketClientLiteException(string message) : base(message)
    {

    }

    public WebsocketClientLiteException(string message, System.Exception inner) : base(message, inner)
    {

    }
}
