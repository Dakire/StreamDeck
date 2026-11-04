using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using StreamDak.Actions;
using StreamDak.Integrations;
using StreamDak.Integrations.Hue;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Correctifs de l'audit de sécurité : pages web importées, import d'intégrations, images, redirections HTTP, empreintes des plugins.</summary>
[Collection("config")]
public class SecurityHardeningTests
{
    sealed class Confirmations : IConfirm
    {
        public string? LastMessage;
        public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false)
        {
            LastMessage = message;
            return Task.FromResult(true);
        }
    }

    static DeckProfile WebProfile(string name, string url) => new()
    {
        Name = name,
        Pages = [new DeckPage { Name = "A", Buttons = [new DeckButton { Widget = WidgetTypes.Web, WidgetOptions = new() { [WidgetOptionKeys.Url] = url } }] }],
    };

    // ---------------------------------------------------------------- pages web importées

    [Fact]
    public void ImportedWebHostsStayPendingAcrossRestartsUntilConfirmed()
    {
        using var dir = new TempConfigDir();
        var own = WebProfile("P", "https://mine.example.org/chat");
        var imported = new[]
        {
            WebProfile("P2", "https://mine.example.org/other"),
            WebProfile("P3", "https://Evil.example.net/x"),
            new DeckProfile { Name = "Q", Pages = [new DeckPage { Name = "W", Kind = PageKinds.Web, Url = "https://pages.example.com/" }] },
        };

        var marked = ImportedWebHosts.Mark(imported, [own]);

        Assert.Equal(new[] { "evil.example.net", "pages.example.com" }, marked.OrderBy(h => h));   // l'hôte déjà choisi par l'utilisateur n'est pas marqué
        Assert.True(ImportedWebHosts.IsPending("EVIL.example.net"));
        Assert.True(File.Exists(Path.Combine(dir.Path, ImportedWebHosts.FileName)));                  // persisté : survit au redémarrage
        Assert.False(ImportedWebHosts.IsPending("mine.example.org"));
        ImportedWebHosts.Confirm("evil.example.net");
        Assert.False(ImportedWebHosts.IsPending("evil.example.net"));
        Assert.True(ImportedWebHosts.IsPending("pages.example.com"));
    }

    [Fact]
    public async Task ImportingAProfileMarksItsWebHostsAsPending()
    {
        using var dir = new TempConfigDir();
        var doc = new DeckDocument(ConfigStore.DefaultConfig());
        var file = Path.Combine(dir.Path, "p.streamdak");
        ProfilePackage.ExportProfile(WebProfile("Importé", "https://evil.example.net/"), file);

        Assert.Equal("Importé", await TransferService.ImportProfileAsync(new Confirmations(), doc, file));

        Assert.True(ImportedWebHosts.IsPending("evil.example.net"));
    }

    // ---------------------------------------------------------------- import d'intégrations

    static DeckConfig WithIntegration(string id, bool enabled, params (string, string)[] values) => new()
    {
        Integrations = new Dictionary<string, IntegrationConfig>(StringComparer.OrdinalIgnoreCase)
        {
            [id] = new IntegrationConfig { Enabled = enabled, Values = values.Length == 0 ? null : values.ToDictionary(v => v.Item1, v => v.Item2) },
        },
    };

    [Fact]
    public void AnImportedIntegrationWithAnotherAddressIsResetToTheCurrentOneAndDisabled()
    {
        var current = WithIntegration("hue", true, ("host", "192.168.1.20"));
        var imported = WithIntegration("hue", true, ("host", "203.0.113.9"), ("scheme", "http"));

        var touched = ImportedIntegrations.Neutralize(imported, current);

        Assert.Equal(new[] { "hue" }, touched);
        var hue = imported.Integrations!["hue"];
        Assert.False(hue.Enabled);
        Assert.Equal("192.168.1.20", hue.Values!["host"]);
        Assert.False(hue.Values.ContainsKey("scheme"));
    }

    [Fact]
    public void AnImportedObsHostIsCheckedAgainstTheDefaultToo()
    {
        // Pas d'OBS configuré ici : « localhost » (valeur par défaut) reste accepté, un autre hôte non
        Assert.Empty(ImportedIntegrations.Neutralize(WithIntegration("obs", true, ("host", "LOCALHOST"), ("port", "4455")), new DeckConfig()));
        var imported = WithIntegration("obs", true, ("host", "evil.example.net"));
        Assert.Equal(new[] { "obs" }, ImportedIntegrations.Neutralize(imported, new DeckConfig()));
        Assert.False(imported.Integrations!["obs"].Enabled);
        Assert.Null(imported.Integrations["obs"].Values);   // pas d'adresse actuelle : le réglage retombe sur sa valeur par défaut
    }

    [Fact]
    public void AnImportedIntegrationWithTheSameAddressIsLeftAlone()
    {
        var current = WithIntegration("hue", true, ("host", "192.168.1.20"));
        var imported = WithIntegration("hue", true, ("host", "192.168.1.20"));
        Assert.Empty(ImportedIntegrations.Neutralize(imported, current));
        Assert.True(imported.Integrations!["hue"].Enabled);
    }

    [Fact]
    public async Task ImportingAConfigurationWarnsAndDisablesIntegrationsThatChangeHost()
    {
        using var dir = new TempConfigDir();
        var doc = new DeckDocument(ConfigStore.DefaultConfig());
        doc.Config.Integrations = WithIntegration("hue", true, ("host", "192.168.1.20")).Integrations;
        var incoming = ConfigStore.DefaultConfig();
        incoming.Integrations = WithIntegration("hue", true, ("host", "203.0.113.9")).Integrations;
        var file = Path.Combine(dir.Path, "c.streamdak");
        ProfilePackage.ExportConfig(incoming, file);
        var ui = new Confirmations();

        Assert.True(await TransferService.ImportConfigAsync(ui, doc, file));

        Assert.Contains("hue", ui.LastMessage);
        Assert.Contains("désactivées", ui.LastMessage);
        var hue = doc.Config.Integrations!["hue"];
        Assert.False(hue.Enabled);
        Assert.Equal("192.168.1.20", hue.Values!["host"]);
    }

    [Theory]
    [InlineData("192.168.1.20", "http", "https")]      // jamais en clair sur le réseau
    [InlineData("192.168.1.20", "https", "https")]
    [InlineData("192.168.1.20", "", "https")]
    [InlineData("localhost", "http", "http")]          // la machine locale (essais) peut rester en http
    [InlineData("127.0.0.1", "http", "http")]
    [InlineData("evil.example.net", "http", "https")]
    public void HueUsesHttpsExceptForTheLocalMachine(string host, string scheme, string expected) =>
        Assert.Equal(expected, HueIntegration.SchemeFor(new IntegrationSettings { Values = new Dictionary<string, string> { ["host"] = host, ["scheme"] = scheme } }));

    // ---------------------------------------------------------------- images

    static byte[] PngDeclaring(int width, int height)
    {
        static uint Crc(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (var b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            return ~crc;
        }
        static void Be(Stream s, uint v) => s.Write([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]);
        static void Chunk(Stream s, string type, byte[] body)
        {
            Be(s, (uint)body.Length);
            var typed = Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
            s.Write(typed);
            Be(s, Crc(typed));
        }
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        using (var ihdr = new MemoryStream())
        {
            Be(ihdr, (uint)width); Be(ihdr, (uint)height);
            ihdr.Write([8, 6, 0, 0, 0]);   // 8 bits, RVBA
            Chunk(ms, "IHDR", ihdr.ToArray());
        }
        Chunk(ms, "IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);   // sans pixels : jamais décodé si les dimensions sont refusées
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    [Fact]
    public void AnImageDeclaringHugeDimensionsIsRefusedBeforeAnyDecoding()
    {
        using var dir = new TempConfigDir();
        var bomb = PngDeclaring(40000, 40000);   // 6,4 Go une fois décodée

        var ex = Assert.Throws<InvalidDataException>(() => IconStore.StoreBytes(bomb));
        Assert.Contains("Dimensions", ex.Message);

        var file = Path.Combine(dir.Path, "bombe.png");
        File.WriteAllBytes(file, bomb);
        ex = Assert.Throws<InvalidDataException>(() => IconStore.Import(file));
        Assert.Contains("Dimensions", ex.Message);

        using var stream = new MemoryStream(bomb);
        Assert.Contains("Dimensions", Assert.Throws<InvalidDataException>(() => IconStore.DecodeLargest(stream)).Message);   // chemin de SiteIcon
    }

    [Fact]
    public void AnOrdinaryImageStillImports()
    {
        using var dir = new TempConfigDir();
        var name = IconStore.StoreBytes(PngDeclaring(1, 1).Length > 0 ? TinyValidPng() : []);
        Assert.True(IconStore.IsValidName(name));
    }

    static byte[] TinyValidPng()
    {
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        var pixels = new byte[4 * 4 * 4];
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
            System.Windows.Media.Imaging.BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 16)));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    // ---------------------------------------------------------------- redirections HTTP des actions

    [Fact]
    public async Task AWebActionNeverFollowsARedirectAndSaysSo()
    {
        using var target = new FakeHttpServer();
        // Le serveur d'origine répond « 302 » avec un en-tête Location (FakeHttpServer ne sait pas en poser) vers le serveur « target »
        using var redirector = new HttpListener();
        int port;
        using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        redirector.Prefixes.Add($"http://localhost:{port}/");
        redirector.Start();
        var served = Task.Run(async () =>
        {
            var ctx = await redirector.GetContextAsync();
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = $"http://localhost:{target.Port}/secret";
            ctx.Response.Close();
        });
        using var http = ActionHttp.CreateClient();
        var engine = new ActionEngine(new ActionContext
        {
            Platform = new SimulatedPlatform().Services,
            Http = http,
            Countdowns = new Countdowns(() => DateTime.UtcNow),
            Delay = (_, _) => Task.CompletedTask,
        });
        var button = new DeckButton { Label = "Appel", Actions = [new DeckAction { Type = ActionTypes.Http, Value = $"http://localhost:{port}/hook", Args = "{\"a\":1}" }] };

        var result = await engine.RunAsync(button, null!);
        await served;

        Assert.False(result.Ok);
        Assert.Contains("redirection", result.Message);
        Assert.Empty(target.Requests);   // le corps et les en-têtes n'ont jamais été renvoyés ailleurs
    }

    // ---------------------------------------------------------------- empreintes des plugins

    static string Legacy(string folder)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, file).ToUpperInvariant() + "\n"));
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    static string InstallPlugin(string root, string name)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        File.Copy(typeof(HueIntegration).Assembly.Location, Path.Combine(folder, name + ".dll"));
        return folder;
    }

    [Fact]
    public void ThePluginHashAlsoCoversTheDepsJsonFile()
    {
        using var dir = new TempConfigDir();
        var folder = InstallPlugin(dir.Path, "Plug");
        var before = PluginTrust.FolderHash(folder);
        Assert.Equal(Legacy(folder), PluginSnapshot.Read(folder).LegacyHash);   // l'ancien calcul est reproduit à l'identique

        File.WriteAllText(Path.Combine(folder, "Plug.deps.json"), "{}");

        Assert.NotEqual(before, PluginTrust.FolderHash(folder));
        Assert.Equal(Legacy(folder), PluginSnapshot.Read(folder).LegacyHash);   // l'ancien calcul ne voit pas ce fichier : d'où le changement
    }

    [Fact]
    public void AnAuthorizationRecordedWithTheOldHashIsAcceptedOnceThenMigrated()
    {
        using var dir = new TempConfigDir();
        var folder = InstallPlugin(dir.Path, "Ancien");
        // .deps.json valide (le résolveur natif du runtime plante sur un fichier mal formé : un plugin refusé n'y arrive jamais, il est vérifié avant)
        File.WriteAllText(Path.Combine(folder, "Ancien.deps.json"), """
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0", "signature": "" },
              "compilationOptions": {},
              "targets": { ".NETCoreApp,Version=v10.0": { "Ancien/1.0.0": { "runtime": { "Ancien.dll": {} } } } },
              "libraries": { "Ancien/1.0.0": { "type": "project", "serviceable": false, "sha512": "" } }
            }
            """);
        File.WriteAllText(Path.Combine(dir.Path, PluginTrust.FileName), $"{{ \"Ancien\": \"{Legacy(folder)}\" }}");   // fichier d'un utilisateur existant

        Assert.Equal(new[] { "Ancien" }, PluginLoader.Load(dir.Path, PluginTrust.Open(dir.Path)).Select(p => p.Name));

        var stored = File.ReadAllText(Path.Combine(dir.Path, PluginTrust.FileName));
        Assert.Contains(PluginTrust.FolderHash(folder), stored);   // migrée vers la nouvelle empreinte
        Assert.DoesNotContain(Legacy(folder), stored);

        // Une fois migrée, une modification du .deps.json est détectée
        File.WriteAllText(Path.Combine(folder, "Ancien.deps.json"), "{\"runtimeTarget\":{}}");
        Assert.Empty(PluginLoader.Load(dir.Path, PluginTrust.Open(dir.Path)));
    }

    [Fact]
    public void ThePluginIsLoadedFromTheVerifiedBytesAndTheLoggedHashIsTheVerifiedOne()
    {
        using var dir = new TempConfigDir();
        var folder = InstallPlugin(dir.Path, "Verifie");
        var trust = PluginTrust.Open(dir.Path);
        trust.TrustAndSave([("Verifie", PluginTrust.FolderHash(folder))]);
        using var log = new MemoryLogSink();

        var plugin = Assert.Single(PluginLoader.Load(dir.Path, PluginTrust.Open(dir.Path)));

        Assert.Equal(string.Empty, plugin.Integrations[0].Assembly.Location);   // chargé depuis un flux : aucune seconde lecture du disque
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, "Verifie.dll")))), plugin.Sha256);
        Assert.True(log.Contains(PluginTrust.FolderHash(folder)[..16]));
    }
}
