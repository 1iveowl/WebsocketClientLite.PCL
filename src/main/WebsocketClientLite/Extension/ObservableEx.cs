using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace WebsocketClientLite.Extension;

// Renamed class to ObservableExtensions to resolve CA1711
public static class ObservableExtensions
{
    public static IObservable<T> FinallyAsync<T>(this IObservable<T> source, Func<Task> task)
    {
        return source
                .Materialize()
                .SelectMany(async n =>
                {
                    switch (n.Kind)
                    {
                        case NotificationKind.OnCompleted:
                        case NotificationKind.OnError:
                            await task().ConfigureAwait(false);
                            return n;
                        case NotificationKind.OnNext:
                            return n;
                        default:
                            throw new NotImplementedException();
                    }
                })
                .Dematerialize();
    }
}
