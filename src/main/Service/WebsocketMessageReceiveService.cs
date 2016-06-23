using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ISocketLite.PCL.Interface;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketMessageReceiveService
    {
        private readonly ITcpSocketClient _client;
        private IObservable<byte[]> _listenForWebsocketData;

        internal WebsocketMessageReceiveService(ITcpSocketClient client)
        {
            _client = client;
        }

        internal void StartListeningForDate(IObservable<byte[]> observeData)
        {
            _listenForWebsocketData = observeData;
            bool IsReadyForNewMessage;
            bool HasReceivedPing;
            bool HasReceivedPong;
            bool IsText;

            string str;

            _listenForWebsocketData.Subscribe(
                bArray =>
                {
                    
                    var bits = new BitArray(bArray);
                    var t = bits[0];
                },
                ex =>
                {
                    
                });
        }
    }
}
