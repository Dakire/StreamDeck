using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Icônes automatiques : favicon d'un site et icône d'une application ou d'un .ico, avec leurs garde-fous.</summary>
public class SiteIconTests
{
    static readonly Uri Page = new("https://exemple.test/app/index.html");

    [Theory]
    [InlineData("exemple.test", true)]
    [InlineData("https://exemple.test/x", true)]
    [InlineData("http://192.168.1.5:8080", true)]
    [InlineData("ftp://exemple.test", false)]
    [InlineData("https://user:mdp@exemple.test", false)]
    [InlineData("file:///C:/x.png", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyHttpAddressesWithoutCredentialsAreAccepted(string? text, bool ok) => Assert.Equal(ok, SiteIcon.IsPageAddress(text, out _));

    [Fact]
    public void TheTouchIconComesFirstThenTheBiggestSizeAndTheFaviconIsTheLastResort()
    {
        const string html = """
            <html><head>
            <link rel="icon" href="/p16.png" sizes="16x16">
            <link rel='shortcut icon' href='/ico/big.png' sizes='96x96'>
            <link rel="apple-touch-icon" href="touch.png" sizes="180x180">
            <link rel="icon" type="image/svg+xml" href="/logo.svg">
            <link rel="stylesheet" href="/style.css">
            <link rel="icon" href="javascript:alert(1)">
            <link rel="icon" href="//cdn.exemple.test/c.png">
            </head></html>
            """;

        var list = SiteIcon.Candidates(html, Page).Select(u => u.AbsoluteUri).ToList();

        Assert.Equal("https://exemple.test/app/touch.png", list[0]);           // icône tactile, adresse relative à la page
        Assert.Equal("https://exemple.test/ico/big.png", list[1]);             // puis la plus grande taille annoncée
        Assert.Contains("https://cdn.exemple.test/c.png", list);
        Assert.DoesNotContain(list, u => u.EndsWith(".svg", StringComparison.Ordinal) || u.StartsWith("javascript", StringComparison.Ordinal) || u.EndsWith(".css", StringComparison.Ordinal));
        Assert.Equal("https://exemple.test/favicon.ico", list[^1]);
    }

    [Fact]
    public void APageWithoutIconsFallsBackToFavicon() =>
        Assert.Equal(new[] { "https://exemple.test/favicon.ico" }, SiteIcon.Candidates("<html></html>", Page).Select(u => u.AbsoluteUri));

    static byte[] Png(int size)
    {
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 200; pixels[i + 1] = 80; pixels[i + 2] = 20; pixels[i + 3] = 255; }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4)));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Petit serveur HTTP local qui sert de l'octet brut (le faux serveur des autres tests ne sert que du texte).</summary>
    sealed class Site : IDisposable
    {
        readonly HttpListener _listener = new();
        public Site(Func<string, (int Status, string Type, byte[] Body)> handler)
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext c;
                    try { c = await _listener.GetContextAsync(); } catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
                    var (status, type, body) = handler(c.Request.Url!.AbsolutePath);
                    c.Response.StatusCode = status;
                    c.Response.ContentType = type;
                    await c.Response.OutputStream.WriteAsync(body);
                    c.Response.Close();
                }
            });
        }
        public int Port { get; }
        public void Dispose() => _listener.Close();
    }

    [Fact]
    public async Task TheFaviconDeclaredByThePageIsDownloadedDecodedAndStoredAsAPng()
    {
        using var site = new Site(path => path switch
        {
            "/" => (200, "text/html", System.Text.Encoding.UTF8.GetBytes("<link rel=\"icon\" href=\"/i.png\" sizes=\"64x64\">")),
            "/i.png" => (200, "image/png", Png(64)),
            _ => (404, "text/plain", Array.Empty<byte>()),
        });

        var name = await SiteIcon.FetchAsync(new Uri($"http://localhost:{site.Port}/"));

        Assert.True(IconStore.IsValidName(name));
        Assert.True(File.Exists(IconStore.Resolve(name)));
    }

    [Fact]
    public async Task WithoutADeclaredIconTheFaviconIcoOfTheSiteIsUsed()
    {
        using var site = new Site(path => path == "/favicon.ico" ? (200, "image/x-icon", Png(32)) : (200, "text/html", System.Text.Encoding.UTF8.GetBytes("<html></html>")));

        Assert.True(IconStore.IsValidName(await SiteIcon.FetchAsync(new Uri($"http://localhost:{site.Port}/page"))));
    }

    [Fact]
    public async Task ASiteWithoutAnyIconOrWithAnUnreadableOneGivesAReadableError()
    {
        using var none = new Site(_ => (404, "text/plain", Array.Empty<byte>()));
        var missing = await Assert.ThrowsAsync<InvalidDataException>(() => SiteIcon.FetchAsync(new Uri($"http://localhost:{none.Port}/")));
        Assert.Contains("icône", missing.Message);

        using var junk = new Site(path => path == "/" ? (200, "text/html", System.Text.Encoding.UTF8.GetBytes("<link rel=icon href=/x.png>")) : (200, "image/png", new byte[] { 1, 2, 3, 4 }));
        var unreadable = await Assert.ThrowsAsync<InvalidDataException>(() => SiteIcon.FetchAsync(new Uri($"http://localhost:{junk.Port}/")));
        Assert.Contains("icône", unreadable.Message);
    }

    [Fact]
    public async Task AnOversizedIconIsRefused()
    {
        using var site = new Site(path => path == "/" ? (200, "text/html", System.Text.Encoding.UTF8.GetBytes("<link rel=icon href=/big.png>")) : (200, "image/png", new byte[SiteIcon.MaxIconBytes + 10]));

        await Assert.ThrowsAsync<InvalidDataException>(() => SiteIcon.FetchAsync(new Uri($"http://localhost:{site.Port}/")));
    }

    [Fact]
    public void AnIcoFileGivesItsBiggestImage()
    {
        // Un .ico à deux tailles (16 et 48 px) : c'est la plus grande qui est retenue
        var small = Png(16);
        var big = Png(48);
        using var ico = new MemoryStream();
        var w = new BinaryWriter(ico);
        w.Write((short)0); w.Write((short)1); w.Write((short)2);
        int offset = 6 + 16 * 2;
        foreach (var (size, data) in new[] { (16, small), (48, big) })
        {
            w.Write((byte)size); w.Write((byte)size); w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32); w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        w.Write(small); w.Write(big);
        w.Flush();
        var dir = Path.Combine(Path.GetTempPath(), "streamdak-ico-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "app.ico");
            File.WriteAllBytes(path, ico.ToArray());

            var name = AppIcon.Store(path)!;

            var stored = IconStore.Load(name)!;
            Assert.Equal(48, ((BitmapSource)stored).PixelWidth);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AProgramIsFoundByItsPathOrOnThePathAndAMissingOneGivesNothing()
    {
        Assert.NotNull(AppIcon.Resolve("cmd"));                                       // cherché dans le PATH
        Assert.NotNull(AppIcon.Resolve("\"%SystemRoot%\\System32\\cmd.exe\""));       // variables développées, guillemets retirés
        Assert.Null(AppIcon.Resolve("programme-qui-n-existe-pas"));
        Assert.Null(AppIcon.Resolve(@"C:\dossier\absent\x.exe"));
        Assert.Null(AppIcon.Resolve(""));
        var steps = new[] { new DeckAction { Type = "hotkey", Value = "ctrl+c" }, new DeckAction { Type = "launch", Value = "cmd" } };
        Assert.NotNull(AppIcon.FirstProgram(steps));
        Assert.Null(AppIcon.FirstProgram(steps.Take(1)));
    }
}
