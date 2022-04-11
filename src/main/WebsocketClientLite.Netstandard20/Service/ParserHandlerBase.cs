//using System;
//using System.Diagnostics;
//using System.IO;
//using System.Threading;
//using System.Threading.Tasks;
//using WebsocketClientLite.PCL.CustomException;

//namespace WebsocketClientLite.PCL.Service
//{
//    internal abstract class ParserHandlerBase
//    {
//        private readonly Func<Stream, byte[], CancellationToken, Task<int>> _readOneByteFunc;

//        protected ParserHandlerBase(Func<Stream, byte[], CancellationToken, Task<int>> readOneByteFunc)
//        {
//            _readOneByteFunc = readOneByteFunc;
//        }

//        protected async Task<byte[]> ReadOneByte(
//            Stream stream,
//            CancellationToken ct)
//        {
//            var oneByte = new byte[1];

//            try
//            {
//                if (stream == null)
//                {
//                    throw new WebsocketClientLiteException("Read stream cannot be null.");
//                }

//                if (!stream.CanRead)
//                {
//                    throw new WebsocketClientLiteException("Websocket connection have been closed.");
//                }

//                var length = await _readOneByteFunc(stream, oneByte, ct);

//                //var bytesRead = await stream.ReadAsync(oneByte, 0, 1);

//                if (length == 0)
//                {
//                    throw new WebsocketClientLiteException("Websocket connection aborted unexpectedly. Check connection and socket security version/TLS version).");
//                }
//            }
//            catch (ObjectDisposedException)
//            {
//                Debug.WriteLine("Ignoring Object Disposed Exception - This is an expected exception");
//            }
//            return oneByte;
//        }

//    }


//}
