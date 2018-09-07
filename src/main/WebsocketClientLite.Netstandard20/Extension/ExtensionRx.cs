using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace WebsocketClientLite.PCL.Extension
{
    public static class ExtensionRx
    {
        public static IObservable<T> FinallyAsync<T>(this IObservable<T> source, Func<Task> action)
        {
            return source
                    .Materialize()
                    .SelectMany(async n =>
                    {
                        switch (n.Kind)
                        {
                            case NotificationKind.OnCompleted:
                            case NotificationKind.OnError:
                                await action();
                                return n;
                            case NotificationKind.OnNext:
                                return n;
                            default:
                                throw new NotImplementedException();
                        }
                    })
                    .Dematerialize()
                ;
        }
    }
}
