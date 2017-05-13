using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Text;

namespace WebsocketClientLite.PCL.Model
{
    public class WebsocketClientLiteException : Exception
    {
        public WebsocketClientLiteException()
        {
            
        }

        public WebsocketClientLiteException(string message) : base(message)
        {

        }

        public WebsocketClientLiteException(string message, Exception inner) : base(message, inner)
        {

        }
    }
}
