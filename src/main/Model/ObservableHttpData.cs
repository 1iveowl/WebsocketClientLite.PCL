using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using WebsocketClientLite.PCL.Model.Base;

namespace WebsocketClientLite.PCL.Model
{
    internal class ObservableHttpData
    {
        internal IObservable<byte[]> Create(IParseControl parseControl, Stream stream, TimeSpan timeout)
        {
            return Observable.Create<byte[]>(
                obs =>
                {
                    var oneByteBuffer = new byte[1];

                    while (!parseControl.IsEndOfMessage
                        && !parseControl.IsRequestTimedOut
                        && !parseControl.IsUnableToParseHttp)
                    {
                        if (stream.Read(oneByteBuffer, 0, oneByteBuffer.Length) != 0)
                        {
                            obs.OnNext(oneByteBuffer);
                        }
                        else
                        {
                            break;
                        }
                    }

                    obs.OnCompleted();
                    return Disposable.Empty;
                })
                .Timeout(timeout);
        }
    }
}
