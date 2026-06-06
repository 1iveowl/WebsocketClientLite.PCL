using System;
using System.Net.Security;
using System.Security.Authentication;
using WebsocketClientLite;
using Xunit;

namespace WebsocketClientLiteTest;

public class ClientWebSocketRxTests
{
    [Theory]
    [InlineData("ws://example.com", false)]
    [InlineData("http://example.com", false)]
    [InlineData("wss://example.com", true)]
    [InlineData("https://example.com", true)]
    public void IsSecureConnectionScheme_MapsSchemes(string uri, bool expected)
    {
        using var client = new ClientWebSocketRx();
        Assert.Equal(expected, client.IsSecureConnectionScheme(new Uri(uri)));
    }

    [Fact]
    public void IsSecureConnectionScheme_UnknownScheme_Throws()
    {
        using var client = new ClientWebSocketRx();
        Assert.Throws<ArgumentException>(() => client.IsSecureConnectionScheme(new Uri("ftp://example.com")));
    }

    [Fact]
    public void ValidateServerCertificate_NoErrors_ReturnsTrue()
    {
        using var client = new ClientWebSocketRx();
        Assert.True(client.ValidateServerCertificate(this, null!, null!, SslPolicyErrors.None));
    }

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable)]
    public void ValidateServerCertificate_Errors_Throw(SslPolicyErrors error)
    {
        using var client = new ClientWebSocketRx();
        Assert.Throws<AuthenticationException>(
            () => client.ValidateServerCertificate(this, null!, null!, error));
    }

    [Theory]
    [InlineData(SslPolicyErrors.None)]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    public void ValidateServerCertificate_IgnoreErrors_AlwaysReturnsTrue(SslPolicyErrors error)
    {
        using var client = new ClientWebSocketRx { IgnoreServerCertificateErrors = true };
        Assert.True(client.ValidateServerCertificate(this, null!, null!, error));
    }

    [Fact]
    public void Defaults_AreSecure()
    {
        using var client = new ClientWebSocketRx();
        Assert.False(client.IgnoreServerCertificateErrors);
        Assert.True(client.CheckCertificateRevocation);
        Assert.Equal(ClientWebSocketRx.DefaultMaxFrameSizeBytes, client.MaxFrameSize);
        Assert.Null(client.Sender);
    }
}
