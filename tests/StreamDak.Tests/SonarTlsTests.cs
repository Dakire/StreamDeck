using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using StreamDak.Integrations.Sonar;
using Xunit;

namespace StreamDak.Tests;

/// <summary>GG répond en HTTPS avec un certificat auto-signé : il doit être accepté sur une adresse locale (ce que les tests HTTP en clair ne prouvaient pas).</summary>
public class SonarTlsTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("::1", true)]
    [InlineData("[::1]", true)]
    [InlineData("192.168.1.10", false)]
    [InlineData("example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyLocalHostsAreTrusted(string? host, bool local) => Assert.Equal(local, SonarApi.IsLocalHost(host));

    [Fact]
    public async Task TheSelfSignedCertificateOfGgIsAcceptedOnTheLoopback()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var cert = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var tcp = await listener.AcceptTcpClientAsync();
                await using var tls = new SslStream(tcp.GetStream());
                await tls.AuthenticateAsServerAsync(cert);
                var buffer = new byte[4096];
                _ = await tls.ReadAsync(buffer);
                var body = "{\"subApps\":{}}";
                await tls.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"));
                await tls.FlushAsync();
            });

            using var client = new HttpClient(SonarApi.CreateLocalHandler()) { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.GetAsync($"https://127.0.0.1:{port}/subApps");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("subApps", await response.Content.ReadAsStringAsync());
            await server;
        }
        finally { listener.Stop(); }
    }
}
