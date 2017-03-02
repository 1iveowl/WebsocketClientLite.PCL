﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ISocketLite.PCL.Model;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL;

class Program
{
    private static IDisposable _subscribeToMessagesReceived;

    const string AllowedChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    static void Main(string[] args)
    {

        StartWebSocketAsync();
        System.Console.WriteLine("Waiting...");
        System.Console.ReadKey();
        _subscribeToMessagesReceived.Dispose();
    }

    private static async void StartWebSocketAsync()
    {
        using (var websocketClient = new MessageWebSocketRx())
        {
            System.Console.WriteLine("Start");

            var websocketLoggerSubscriber = websocketClient.ObserveConnectionStatus.Subscribe(
                s =>
                {
                    System.Console.WriteLine(s.ToString());
                });

            _subscribeToMessagesReceived = websocketClient.ObserveTextMessagesReceived.Subscribe(
                msg =>
                {
                    System.Console.WriteLine($"Reply from test server: {msg}");
                },
                () =>
                {
                    System.Console.WriteLine($"Subscription Completed");
                });

            // ### Optional Subprotocols ###
            // The echo.websocket.org does not support any sub-protocols and hence this test does not add any.
            // Adding a sub-protocol that the server does not support causes the client to close down the connection.
            List<string> subprotocols = null; //new List<string> {"soap", "json"};

            var headers = new Dictionary<string, string> { { "Pragma", "no-cache" }, { "Cache-Control", "no-cache" } };


            //await websocketClient.ConnectAsync(
            //    new Uri("ws://192.168.0.7:3000/socket.io/?EIO=2&transport=websocket"),
            //    //new Uri("wss://echo.websocket.org:443"),
            //    //cts,
            //    origin: null,
            //    ignoreServerCertificateErrors: true,
            //    subprotocols: subprotocols,
            //    tlsProtocolVersion: TlsProtocolVersion.Tls12);

            ////System.Console.WriteLine("Sending: Test Single Frame");
            //await websocketClient.SendTextAsync("Test rpi3");


            //await websocketClient.CloseAsync();


            await websocketClient.ConnectAsync(
                //new Uri("ws://localhost:3000/socket.io/?EIO=2&transport=websocket"),
                new Uri("wss://echo.websocket.org:443"),
                //cts,
                ignoreServerCertificateErrors: true,
                headers: headers,
                subprotocols: subprotocols,
                tlsProtocolVersion: TlsProtocolVersion.Tls12);

            System.Console.WriteLine("Sending: Test Single Frame");
            await websocketClient.SendTextAsync("Test Single Frame");

            await websocketClient.SendTextAsync("Test Single Frame again");

            await websocketClient.SendTextAsync(TestString(128, 200));

            await websocketClient.SendTextAsync(TestString(65538, 65550));

            //var strArray = new[] { "Test ", "multiple ", "frames" };

            //await websocketClient.SendTextAsync(strArray);

            //await websocketClient.SendTextMultiFrameAsync("Start ", FrameType.FirstOfMultipleFrames);
            //await Task.Delay(TimeSpan.FromMilliseconds(200));
            //await websocketClient.SendTextMultiFrameAsync("Continue... #1 ", FrameType.Continuation);
            //await Task.Delay(TimeSpan.FromMilliseconds(300));
            //await websocketClient.SendTextMultiFrameAsync("Continue... #2 ", FrameType.Continuation);
            //await Task.Delay(TimeSpan.FromMilliseconds(150));
            //await websocketClient.SendTextMultiFrameAsync("Continue... #3 ", FrameType.Continuation);
            //await Task.Delay(TimeSpan.FromMilliseconds(400));
            //await websocketClient.SendTextMultiFrameAsync("Stop.", FrameType.LastInMultipleFrames);

            //await websocketClient.CloseAsync();

            //await websocketClient.ConnectAsync(
            //    new Uri("ws://192.168.0.7:3000/socket.io/?EIO=2&transport=websocket"),
            //    //new Uri("wss://echo.websocket.org:443"),
            //    //cts,
            //    ignoreServerCertificateErrors: true,
            //    subprotocols: subprotocols,
            //    tlsProtocolVersion: TlsProtocolVersion.Tls12);

            //await websocketClient.SendTextAsync("Test localhost");

            //await websocketClient.CloseAsync();
        }


    }

    private static string TestString(int minlength, int maxlenght)
    {

        var rng = new Random();

        return RandomStrings(AllowedChars, minlength, maxlenght, 25, rng);
    }

    private static string RandomStrings(
        string allowedChars,
        int minLength,
        int maxLength,
        int count,
        Random rng)
    {
        var chars = new char[maxLength];
        var setLength = allowedChars.Length;

        var length = rng.Next(minLength, maxLength + 1);

        for (var i = 0; i < length; ++i)
        {
            chars[i] = allowedChars[rng.Next(setLength)];
        }

        return new string(chars, 0, length);
    }
}