using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebsocketClientLite.PCL.Model;

namespace WebsocketClientLite.PCL.Helper
{
    public static class Extensions
    {
        //private static async Task<byte[]> ReadOneByteAtTheTimeAsync(Stream stream, CancellationTokenSource cancellationTokenSource)
        //{
        //    //if (!isConnectionOpen) return null;

        //    var oneByteArray = new byte[1];

        //    try
        //    {
        //        if (stream == null)
        //        {
        //            throw new WebsocketClientLiteException("Readstream cannot be null.");
        //        }

        //        if (!stream.CanRead)
        //        {
        //            throw new WebsocketClientLiteException("Websocket connection have been closed.");
        //        }

        //        var bytesRead = await stream.ReadAsync(oneByteArray, 0, 1);

        //        if (bytesRead < oneByteArray.Length)
        //        {
        //            cancellationTokenSource.Cancel();
        //            throw new WebsocketClientLiteException("Websocket connection aborted unexpectantly. Check connection and socket security version/TLS version).");
        //        }
        //    }
        //    catch (ObjectDisposedException)
        //    {
        //        Debug.WriteLine("Ignoring Object Disposed Exception - This is an expected exception");
        //    }
        //    return oneByteArray;
        //}

        //internal static IObservable<byte[]> ReadOneByteAtTheTimeObservable(this Stream stream, CancellationTokenSource cancellationTokenSource, bool isConnectionOpen)
        //{
        //    return Observable.While(
        //        () => !cancellationTokenSource.IsCancellationRequested,
        //        Observable.FromAsync(() => ReadOneByteAtTheTimeAsync(stream, cancellationTokenSource, isConnectionOpen)));
        //}
    }
}
