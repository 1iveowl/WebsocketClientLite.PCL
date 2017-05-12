using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WebsocketClientLite.PCL.Helper
{
    public static class Extensions
    {
        private static async Task<byte[]> ReadOneByteAtTheTimeAsync(Stream stream, CancellationTokenSource cancellationTokenSource)
        {
            var oneByteArray = new byte[1];
            int bytesRead = 0;

            try
            {
                if (stream == null)
                {
                    throw new Exception("Readstream cannot be null.");
                }

                if (!stream.CanRead)
                {
                    throw new Exception("Websocket connection have been closed.");
                }

                bytesRead = await stream.ReadAsync(oneByteArray, 0, 1);
                
                //Debug.WriteLine(oneByteArray[0].ToString());

                if (bytesRead < oneByteArray.Length)
                {
                    //_isConnected = false;
                    cancellationTokenSource.Cancel();
                    throw new Exception("Websocket connection aborted unexpectantly. Check connection and socket security version/TLS version).");
                }
            }
            catch (ObjectDisposedException)
            {
                Debug.WriteLine("Ignoring Object Disposed Exception - This is an expected exception");
            }
            return oneByteArray;
        }

        internal static IObservable<byte[]> ReadOneByteAtTheTimeObservable(this Stream stream, CancellationTokenSource cancellationTokenSource)
        {
            return Observable.While(
                () => !cancellationTokenSource.IsCancellationRequested,
                Observable.FromAsync(() => ReadOneByteAtTheTimeAsync(stream, cancellationTokenSource)));
        }
    }
}
