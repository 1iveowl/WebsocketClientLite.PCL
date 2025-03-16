using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace WebsocketClientLite.Extension;

public static class ObservableEx
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
                            await task();
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

    internal static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> enumerable)
    {
        foreach (var item in enumerable)
        {
            yield return await Task.FromResult(item);
        }
    }

    //public static IObservable<TSource> UsingAsync<TSource, TResource>(
    //    Func<Task<TResource>> resourceFactoryAsync,
    //    Func<TResource, IObservable<TSource>> observableFactory) where TResource : IDisposable =>
    //        Observable.FromAsync(resourceFactoryAsync)            
    //            .SelectMany(resource => Observable.Using(() => resource, observableFactory));
}
