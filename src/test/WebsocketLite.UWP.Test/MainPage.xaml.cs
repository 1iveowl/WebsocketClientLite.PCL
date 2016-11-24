using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using ISocketLite.PCL.Model;
using WebsocketClientLite.PCL;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace WebsocketLite.UWP.Test
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>

    public sealed partial class MainPage : Page

    {
        private IDisposable _subscribeToMessagesReceived;
        public MainPage()
        {
            this.InitializeComponent();
            StartTest();
        }
        private async void StartTest()
        {
            var websocketClient = new MessageWebSocketRx();

            _subscribeToMessagesReceived = websocketClient
                .ObserveTextMessagesReceived
                .ObserveOnDispatcher()
                .Subscribe(
                msg =>
                {
                    Received.Text = msg;
                });

            var cts = new CancellationTokenSource();

            cts.Token.Register(() =>
            {
                _subscribeToMessagesReceived.Dispose();
            });

            await
                websocketClient.ConnectAsync(
                    new Uri("wss://echo.websocket.org:443"),
                    cts,
                    ignoreServerCertificateErrors: true,
                    subprotocols: null,
                    tlsProtocolVersion: TlsProtocolVersion.Tls12);

            var testString = "Test Single Frame";

            Sending.Text = testString;
            await Task.Delay(TimeSpan.FromSeconds(1));

            await websocketClient.SendTextAsync("Test Single Frame");
        }
    }
}
