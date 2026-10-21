using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace TouchDeck.Tests;

[Collection("config")]
public class PackageTests
{
    static string TempFile(TempConfigDir dir, string name) => Path.Combine(dir.Path, name);

    static byte[] TinyPng(byte seed = 0)
    {
        var pixels = new byte[4 * 4 * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 7 + seed);
        var bmp = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    static void WriteZip(string path, params (string Name, byte[] Data)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            var e = zip.CreateEntry(name);
            using var s = e.Open();
            s.Write(data);
        }
    }

    static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    DeckProfile SampleProfile(string imageName)
    {
        var p = new DeckProfile { Name = "Jeu", Columns = 5, Rows = 3, AppMatch = "steam" };
        p.Pages.Add(new DeckPage { Name = "A" });
        p.Pages[0].Buttons.Add(new DeckButton { Label = "Ouvre", Actions = { new DeckAction { Type = "launch", Value = "notepad.exe" } }, ImagePath = imageName });
        var folder = DeckDocument.NewFolder("Dossier");
        folder.Children!.Add(new DeckButton { Label = "Dedans", Actions = { new DeckAction { Type = "script", Value = "Get-Date" } } });
        p.Pages[0].Buttons.Add(folder);
        return p;
    }

    [Fact]
    public void ProfileRoundTripsWithItsImagesAndCountsRiskyActions()
    {
        using var dir = new TempConfigDir();
        var name = IconStore.StoreBytes(TinyPng());
        var path = TempFile(dir, "jeu.touchdeck");
        ProfilePackage.ExportProfile(SampleProfile(name), path);

        File.Delete(IconStore.Resolve(name)!);        // simule l'import sur une autre machine
        var pkg = ProfilePackage.Read(path);

        Assert.Null(pkg.Config);
        Assert.Equal("Jeu", pkg.Profile!.Name);
        Assert.Equal("steam", pkg.Profile.AppMatch);
        Assert.Equal(2, pkg.RiskyActions);             // launch + script (dans le dossier)
        Assert.Contains(name, pkg.Icons.Keys);
        Assert.Empty(pkg.Warnings);

        ProfilePackage.InstallIcons(pkg);
        Assert.True(File.Exists(IconStore.Resolve(name)));
        Assert.NotNull(IconStore.Load(name));
    }

    [Fact]
    public void ConfigRoundTrips()
    {
        using var dir = new TempConfigDir();
        var cfg = ConfigStore.DefaultConfig();
        cfg.Favorites.Add("sys.desktop");
        var path = TempFile(dir, "tout.touchdeck");
        ProfilePackage.ExportConfig(cfg, path);
        var pkg = ProfilePackage.Read(path);
        Assert.Null(pkg.Profile);
        Assert.Equal(new[] { "sys.desktop" }, pkg.Config!.Favorites);
        Assert.Equal(cfg.Profiles[0].Pages.Count, pkg.Config.Profiles[0].Pages.Count);
    }

    [Fact]
    public void PlainJsonConfigCanBeImportedIncludingTheOldFormat()
    {
        using var dir = new TempConfigDir();
        var path = TempFile(dir, "ancien.json");
        File.WriteAllText(path, Samples.LegacyJson);
        var pkg = ProfilePackage.Read(path);
        Assert.NotNull(pkg.Config);
        Assert.Equal(2, pkg.Config!.Profiles[0].Pages.Count);
    }

    [Fact]
    public void UnknownActionTypesAreRefused()
    {
        using var dir = new TempConfigDir();
        var path = TempFile(dir, "mauvais.touchdeck");
        var json = """{"Name":"X","Pages":[{"Name":"A","Buttons":[{"Label":"b","Actions":[{"Type":"rm-rf","Value":"/"}]}]}]}""";
        WriteZip(path, ("profile.json", Utf8(json)));
        var ex = Assert.Throws<InvalidDataException>(() => ProfilePackage.Read(path));
        Assert.Contains("inconnu", ex.Message);
    }

    [Fact]
    public void ImageNamesFromAnArchiveCanNeverEscapeTheIconFolder()
    {
        using var dir = new TempConfigDir();
        var path = TempFile(dir, "piege.touchdeck");
        var good = IconStore.StoreBytes(TinyPng(3));
        var json = "{\"Name\":\"X\",\"Pages\":[{\"Name\":\"A\",\"Buttons\":[{\"Label\":\"b\",\"ImagePath\":\"..\\\\..\\\\evil.png\",\"Actions\":[{\"Type\":\"delay\",\"Value\":\"1\"}]}]}]}";
        WriteZip(path,
            ("profile.json", Utf8(json)),
            ("icons/../../evil.png", TinyPng(9)),
            ("icons/..\\..\\evil2.png", TinyPng(9)),
            ("../outside.txt", Utf8("x")),
            ("icons/" + good, TinyPng(3)));
        var pkg = ProfilePackage.Read(path);
        Assert.Null(pkg.Profile!.Pages[0].Buttons[0].ImagePath);          // nom invalide retiré
        Assert.Equal(new[] { good }, pkg.Icons.Keys.ToArray());            // seuls les noms valides sont gardés
        Assert.NotEmpty(pkg.Warnings);
        ProfilePackage.InstallIcons(pkg);
        Assert.False(File.Exists(Path.Combine(dir.Path, "evil.png")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(dir.Path)!, "evil.png")));
    }

    [Fact]
    public void FakePngAndOversizedEntriesAreRefused()
    {
        using var dir = new TempConfigDir();
        var name = new string('a', 32) + ".png";
        var path = TempFile(dir, "faux.touchdeck");
        WriteZip(path, ("profile.json", Utf8("""{"Name":"X"}""")), ("icons/" + name, Utf8("ceci n'est pas un PNG")));
        var pkg = ProfilePackage.Read(path);
        Assert.Empty(pkg.Icons);
        Assert.Contains(pkg.Warnings, w => w.Contains("PNG"));

        var big = TempFile(dir, "gros.touchdeck");
        WriteZip(big, ("profile.json", new byte[(int)ProfilePackage.MaxJsonBytes + 10]));
        Assert.Throws<InvalidDataException>(() => ProfilePackage.Read(big));
    }

    [Fact]
    public void CorruptArchiveAndEmptyArchiveAreRefused()
    {
        using var dir = new TempConfigDir();
        var corrupt = TempFile(dir, "casse.touchdeck");
        File.WriteAllBytes(corrupt, new byte[] { (byte)'P', (byte)'K', 1, 2, 3, 4, 5, 6 });
        Assert.Throws<InvalidDataException>(() => ProfilePackage.Read(corrupt));

        var empty = TempFile(dir, "vide.touchdeck");
        WriteZip(empty, ("manifest.json", Utf8("{}")));
        Assert.Throws<InvalidDataException>(() => ProfilePackage.Read(empty));
        Assert.Throws<InvalidDataException>(() => ProfilePackage.Read(TempFile(dir, "inexistant.touchdeck")));

        var notJson = TempFile(dir, "texte.json");
        File.WriteAllText(notJson, "bonjour");
        Assert.Throws<InvalidDataException>(() => ProfilePackage.Read(notJson));
    }

    [Fact]
    public void TooManyButtonsAreRefused()
    {
        using var dir = new TempConfigDir();
        var p = new DeckProfile { Name = "Énorme" };
        var page = new DeckPage { Name = "A" };
        for (int i = 0; i < ProfilePackage.MaxButtons + 1; i++) page.Buttons.Add(new DeckButton { Label = "b" });
        p.Pages.Add(page);
        var path = TempFile(dir, "enorme.touchdeck");
        WriteZip(path, ("profile.json", Utf8(JsonSerializer.Serialize(p, ConfigStore.Options))));
        Assert.Throws<InvalidDataException>(() => ProfilePackage.Read(path));
    }

    [Fact]
    public void ImportedProfileGetsAUniqueNameAndNoDuplicateAppMatch()
    {
        var d = Samples.Doc("a");
        d.SetProfileApp("P", "steam");
        var imported = SampleProfile("");
        imported.Name = "P";
        Assert.True(d.AddImportedProfile(imported).Ok);
        Assert.Equal("P (2)", d.Config.Profiles[1].Name);
        Assert.Null(d.Config.Profiles[1].AppMatch);
        Assert.True(d.Undo());
        Assert.Single(d.Config.Profiles);
    }

    // ---------- images ----------

    [Fact]
    public void StoredNamesAreContentHashes()
    {
        using var dir = new TempConfigDir();
        var a = IconStore.StoreBytes(TinyPng(1));
        var b = IconStore.StoreBytes(TinyPng(1));
        var c = IconStore.StoreBytes(TinyPng(2));
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.True(IconStore.IsValidName(a));
        Assert.Equal(Path.Combine(ConfigStore.IconsDir, a), IconStore.Resolve(a));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("..\\x.png")] [InlineData("../x.png")] [InlineData("C:\\Windows\\win.ini")]
    [InlineData("abc.png")] [InlineData("ABCDEF0123456789ABCDEF0123456789.png")] [InlineData("0123456789abcdef0123456789abcdef.exe")]
    public void OnlyHashNamesAreAccepted(string? name)
    {
        Assert.False(IconStore.IsValidName(name));
        Assert.Null(IconStore.Resolve(name));
        Assert.Null(IconStore.Load(name));
    }

    [Fact]
    public void ImportResizesLargeImagesAndRejectsBadFiles()
    {
        using var dir = new TempConfigDir();
        var big = BitmapSource.Create(2000, 1000, 96, 96, PixelFormats.Bgra32, null, new byte[2000 * 1000 * 4], 2000 * 4);
        var enc = new JpegBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(big));
        var src = TempFile(dir, "grande.jpg");
        using (var fs = File.Create(src)) enc.Save(fs);

        var name = IconStore.Import(src);
        var loaded = (BitmapSource)IconStore.Load(name)!;
        Assert.Equal(IconStore.StoredSize, loaded.PixelWidth);
        Assert.Equal(IconStore.StoredSize / 2, loaded.PixelHeight);       // le ratio est conservé

        var fake = TempFile(dir, "faux.png");
        File.WriteAllText(fake, "pas une image");
        Assert.Throws<InvalidDataException>(() => IconStore.Import(fake));
        var wrongExt = TempFile(dir, "script.ps1");
        File.WriteAllText(wrongExt, "Get-Date");
        Assert.Throws<InvalidDataException>(() => IconStore.Import(wrongExt));
        Assert.Throws<InvalidDataException>(() => IconStore.Import(TempFile(dir, "absent.png")));
    }
}

public class ThemeTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void TextMeetsWcagAaOnItsBackgrounds(bool dark)
    {
        var p = dark ? Theme.DarkPalette : Theme.LightPalette;
        foreach (var bg in new[] { p.Window, p.Panel, p.Surface, p.SurfaceHi })
        {
            Assert.True(Theme.Contrast(p.Text, bg) >= 4.5, $"Text sur {bg}");
            Assert.True(Theme.Contrast(p.TextSoft, bg) >= 4.5, $"TextSoft sur {bg}");
            Assert.True(Theme.Contrast(p.Dim, bg) >= 4.5, $"Dim sur {bg}");
            Assert.True(Theme.Contrast(p.AccentText, bg) >= 4.5, $"AccentText sur {bg}");
            Assert.True(Theme.Contrast(p.Danger, bg) >= 4.5, $"Danger sur {bg}");
            Assert.True(Theme.Contrast(p.Success, bg) >= 3.0, $"Success sur {bg}");
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void ControlsAndFocusRingsMeetNonTextContrast(bool dark)
    {
        var p = dark ? Theme.DarkPalette : Theme.LightPalette;
        Assert.True(Theme.Contrast(p.Focus, p.Window) >= 3.0, "anneau de focus sur la fenêtre");
        Assert.True(Theme.Contrast(p.Focus, p.Panel) >= 3.0, "anneau de focus sur un panneau");
        Assert.True(Theme.Contrast(p.Placeholder, p.Panel) >= 3.0, "cadre des cases vides");
        Assert.True(Theme.Contrast(p.Accent, p.Window) >= 1.0);
        Assert.True(Theme.Contrast(p.AccentText, p.Window) >= 3.0, "anneau de sélection");
    }

    [Fact]
    public void TextOnTheAccentColorIsReadableInEveryTheme()
    {
        foreach (var p in new[] { Theme.DarkPalette, Theme.AmoledPalette, Theme.LightPalette })
        {
            Assert.True(Theme.Contrast(p.OnAccent, p.Accent) >= 4.5, "texte sur l'accent");
            Assert.True(Theme.Contrast(p.OnAccentContainer, p.AccentContainer) >= 4.5, "texte sur le conteneur d'accent");
            Assert.True(Theme.Contrast(p.OnSecondaryContainer, p.SecondaryContainer) >= 4.5, "texte sur l'onglet actif");
        }
    }

    [Fact]
    public void StatusBannerIsReadable()
    {
        foreach (var p in new[] { Theme.DarkPalette, Theme.LightPalette })
            Assert.True(Theme.Contrast(p.StatusText, p.StatusBg) >= 4.5);
    }

    [Fact]
    public void CatalogAndSwatchColorsGiveReadableWhiteText()
    {
        foreach (var a in ActionCatalog.All) Assert.True(Theme.Contrast("#FFFFFF", a.Color) >= 4.5, $"{a.Id} {a.Color}");
        foreach (var c in ViewModels.PropertiesViewModel.Palette) Assert.True(Theme.Contrast("#FFFFFF", c) >= 4.5, $"palette {c}");
        Assert.True(Theme.Contrast("#FFFFFF", DeckDocument.NewFolder().Color) >= 4.5, "dossier");
        foreach (var b in DeckDocument.AllButtons(ConfigStore.DefaultConfig().Profiles[0].Pages.SelectMany(p => p.Buttons)))
            Assert.True(Theme.Contrast("#FFFFFF", b.Color) >= 4.5, $"défaut {b.Label} {b.Color}");
    }

    [Fact]
    public void TileTitlesStayReadableOnTintedBodies()
    {
        // Le titre est écrit sur le corps teinté de la touche : contraste AA pour toutes les couleurs proposées, dans les deux thèmes
        var colors = ActionCatalog.All.Select(a => a.Color).Concat(ViewModels.PropertiesViewModel.Palette).Distinct().ToList();
        var previous = Theme.Dark;
        try
        {
            foreach (var dark in new[] { true, false })
            {
                typeof(Theme).GetProperty(nameof(Theme.Dark))!.SetValue(null, dark);
                var text = Theme.Current.Text;
                foreach (var c in colors)
                {
                    var (top, bottom) = Theme.TileFillColors(c);
                    Assert.True(Theme.Contrast(text, top) >= 4.5, $"{(dark ? "sombre" : "clair")} {c} : {Theme.Contrast(text, top):0.0}");
                    Assert.True(Theme.Contrast(text, bottom) >= 4.5, $"{(dark ? "sombre" : "clair")} {c} bas");
                }
            }
        }
        finally { typeof(Theme).GetProperty(nameof(Theme.Dark))!.SetValue(null, previous); }
        Assert.Equal("#808080", Theme.Mix("#000000", "#FFFFFF", 0.502));
    }

    [Fact]
    public void TileGradientKeepsTheChosenColorOnTopAndDarkensBelow()
    {
        var g = (LinearGradientBrush)Theme.TileGradient("#2563EB");
        Assert.Equal((Color)ColorConverter.ConvertFromString("#2563EB"), g.GradientStops[0].Color);
        var bottom = g.GradientStops[1].Color;
        Assert.True(bottom.R < 0x25 && bottom.G < 0x63 && bottom.B < 0xEB);
        Assert.True(g.IsFrozen);
        Assert.Same(g, Theme.TileGradient("#2563eb"));                    // mis en cache, insensible à la casse
        Assert.ThrowsAny<Exception>(() => Theme.TileGradient("pas une couleur"));
        var face = ViewModels.TileFaceData.From(new DeckButton { Color = "pas une couleur" });
        Assert.NotNull(face.Background);                                  // repli sans exception
    }

    [Fact]
    public void ContrastMath()
    {
        Assert.Equal(21, Theme.Contrast("#FFFFFF", "#000000"), 1);
        Assert.Equal(1, Theme.Contrast("#777777", "#777777"), 3);
    }
}
