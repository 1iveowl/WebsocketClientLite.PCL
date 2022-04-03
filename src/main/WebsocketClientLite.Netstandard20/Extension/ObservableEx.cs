using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace WebsocketClientLite.PCL.Extension
{
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

        public static IObservable<TSource> UsingAsync<TSource, TResource>(
            Func<Task<TResource>> resourceFactoryAsync,
            Func<TResource, IObservable<TSource>> observableFactory) where TResource : IDisposable =>
                Observable.FromAsync(resourceFactoryAsync)            
                    .SelectMany(resource => Observable.Using(() => resource, observableFactory));

        //public static IObservable<TSource> UsingAsync<TSource, TResource1, TResource2>(
        //    Func<Task<TResource1>> resourceFactoryAsync1,
        //    Func<Task<TResource2>> resourceFactoryAsync2,
        //    Func<TResource1, TResource2, IObservable<TSource>> observableFactory) where TResource1 : IDisposable =>
        //        Observable.FromAsync(resourceFactoryAsync1)
        //            .SelectMany(resource1 => Observable.Using(() => resource1, observableFactory));
    }
}
