using System;
using System.IO;
using System.Linq;
using System.Reactive.Linq;

namespace WebsocketClientLite.PCL.Helper
{
    public static class Extensions
    {
        public static IObservable<byte> ReadDataSObservable(this Stream stream)
        {
            return Observable.Defer(async () =>
            {
                var buffer = new byte[1];
                var readBytes = await stream.ReadAsync(buffer, 0, buffer.Length);
                return buffer.Take(readBytes).ToObservable();
            });
        }
    }
}
