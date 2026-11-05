using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Zone d'icône d'une touche et taille d'affichage d'une image (logique pure, ADR 0036).</summary>
public class IconLayoutTests
{
    public static IEnumerable<object[]> Tiles()
    {
        foreach (var (w, h) in new[] { (64.0, 64.0), (96.0, 96.0), (140.0, 120.0), (220.0, 220.0), (400.0, 180.0), (90.0, 300.0) })
            foreach (double title in new[] { 0.0, 14.0, 30.0, 60.0 })
                foreach (var place in new[] { TitlePlace.Below, TitlePlace.Above, TitlePlace.None })
                    yield return new object[] { w, h, title, place };
    }

    [Theory]
    [MemberData(nameof(Tiles))]
    public void TheIconZoneStaysInsideTheTileAndNeverCoversTheTitle(double w, double h, double title, TitlePlace place)
    {
        double pad = Math.Max(4, Math.Min(w, h) * 0.07);
        var (icon, text) = IconLayout.Zones(w, h, pad, place == TitlePlace.None ? 0 : title, Math.Min(w, h) * 0.06, place);
        var inner = new LayoutRect(pad, pad, w - 2 * pad, h - 2 * pad);
        Assert.True(inner.Contains(icon), $"icône {icon} hors de {inner}");
        Assert.False(icon.Overlaps(text), $"icône {icon} sur le titre {text}");
        Assert.Equal(icon.Width, icon.Height, 6);
        Assert.True(icon.Width <= IconLayout.MaxZoneDip);
        if (place != TitlePlace.None && title > 0)
        {
            Assert.True(inner.Contains(text), $"titre {text} hors de {inner}");
            Assert.True(place == TitlePlace.Below ? text.Y >= icon.Bottom : text.Bottom <= icon.Y, "ordre icône / titre");
            Assert.True(icon.Width <= (h - 2 * pad) * IconLayout.ZoneWithTitle + 1e-9);
        }
    }

    [Fact]
    public void ABigTileGetsAProportionalButCappedZone()
    {
        var (normal, _) = IconLayout.Zones(120, 120, 8, 20, 6, TitlePlace.Below);
        Assert.InRange(normal.Width, (120 - 16) * 0.5, (120 - 16) * 0.6);
        var (huge, _) = IconLayout.Zones(600, 600, 20, 30, 10, TitlePlace.Below);
        Assert.Equal(IconLayout.MaxZoneDip, huge.Width);
    }

    [Theory]
    [InlineData(1.0)] [InlineData(1.5)] [InlineData(2.0)]
    public void ABigImageIsReducedToTheZoneAtEveryScale(double dpi)
    {
        var (w, h, pixelated) = IconLayout.ImageSize(512, 512, 60, dpi);
        Assert.Equal(60, w, 6);
        Assert.Equal(60, h, 6);
        Assert.False(pixelated);
    }

    [Theory]
    [InlineData(1.0, 3, 96)]   // 32 px dans 64 DIP : 2 × (lissage interdit au-delà de 1,25)… borné à 3 × si la zone le permet
    [InlineData(2.0, 3, 48)]   // à 200 %, 64 DIP = 128 px physiques : 32 × 3 = 96 px = 48 DIP
    [InlineData(1.5, 3, 64)]   // 96 px physiques / 1,5
    public void ATinyImageIsShownAtAnIntegerMultipleNeverBlurred(double dpi, int expectedScale, double expectedDip)
    {
        var (w, _, pixelated) = IconLayout.ImageSize(32, 32, dpi == 1.0 ? 100 : 64, dpi);
        Assert.True(pixelated);
        Assert.Equal(expectedDip, w, 6);
        Assert.Equal(32 * expectedScale / dpi, w, 6);
    }

    [Fact]
    public void ASlightlySmallerImageIsSmoothlyEnlargedAndAMuchSmallerOneKeepsItsSize()
    {
        var (w, _, pixelated) = IconLayout.ImageSize(100, 100, 120, 1);   // 1,2 × : lissé
        Assert.Equal(120, w, 6);
        Assert.False(pixelated);
        var (native, _, flat) = IconLayout.ImageSize(48, 48, 80, 1);       // 1,67 × : ni flou ni multiple possible → taille réelle
        Assert.Equal(48, native, 6);
        Assert.False(flat);
    }

    [Fact]
    public void ANonSquareImageKeepsItsProportions()
    {
        var (w, h, _) = IconLayout.ImageSize(400, 100, 80, 1.5);
        Assert.Equal(80, w, 6);
        Assert.Equal(20, h, 6);
    }

    [Fact]
    public void ADegenerateInputGivesNothing() => Assert.Equal((0.0, 0.0, false), IconLayout.ImageSize(0, 10, 50, 1));
}

/// <summary>Icônes des applications : grande taille, recadrage des marges, remplacement des anciennes icônes de 32 px.</summary>
[Collection("ui")]
public class AppIconQualityTests
{
    readonly WpfHost _host;
    public AppIconQualityTests(WpfHost host) => _host = host;

    static BitmapSource Square(int canvas, int drawn, int x0 = 0, int y0 = 0)
    {
        var pixels = new byte[canvas * canvas * 4];
        for (int y = y0; y < y0 + drawn; y++)
            for (int x = x0; x < x0 + drawn; x++)
            {
                int i = (y * canvas + x) * 4;
                pixels[i] = 30; pixels[i + 1] = 120; pixels[i + 2] = 220; pixels[i + 3] = 255;
            }
        var b = BitmapSource.Create(canvas, canvas, 96, 96, PixelFormats.Bgra32, null, pixels, canvas * 4);
        b.Freeze();
        return b;
    }

    [Fact]
    public void ASmallIconInTheCornerOfAJumboCanvasIsCroppedToItsDrawing()
    {
        var cropped = AppIcon.TrimPadding(Square(256, 32));
        Assert.Equal(32, cropped.PixelWidth);
        Assert.Equal(32, cropped.PixelHeight);
    }

    [Fact]
    public void ARealLargeIconIsKeptAsIs()
    {
        var full = Square(256, 220, 18, 18);
        Assert.Same(full, AppIcon.TrimPadding(full));
    }

    [Fact]
    public void AProgramIconIsStoredLargerThanTheOld32Pixels()
    {
        var notepad = AppIcon.Resolve("notepad") ?? AppIcon.Resolve("cmd");
        Assert.NotNull(notepad);
        var name = _host.Run(() => AppIcon.Store(notepad));
        Assert.NotNull(name);
        var stored = _host.Run(() => (BitmapSource)IconStore.Load(name)!);
        Assert.True(Math.Max(stored.PixelWidth, stored.PixelHeight) > 32, $"{stored.PixelWidth} px");
    }

    [Fact]
    public void TheOldIconOfAProgramIsRecognisedAndReplacedNeverAnImportedOne()
    {
        var cmd = AppIcon.Resolve("cmd")!;
        _host.Run(() =>
        {
            var legacy = AppIcon.LegacyName(cmd);
            Assert.NotNull(legacy);
            var old = new DeckButton { Label = "Invite", ImagePath = legacy, ImageFit = "contain", Actions = { new DeckAction { Type = ActionTypes.Launch, Value = cmd } } };
            var imported = new DeckButton { Label = "Perso", ImagePath = "0123456789abcdef0123456789abcdef.png", ImageFit = "contain", Actions = { new DeckAction { Type = ActionTypes.Launch, Value = cmd } } };
            var cfg = ConfigStore.DefaultConfig();
            cfg.Profiles[0].Pages[0].Buttons.Add(old);
            cfg.Profiles[0].Pages[0].Buttons.Add(imported);

            var candidates = IconUpgrade.Candidates(cfg, _ => 32, IconUpgrade.ProgramOf);
            Assert.Equal(2, candidates.Count);
            var plan = IconUpgrade.Plan(candidates, AppIcon.LegacyName, _ => "fedcba9876543210fedcba9876543210.png");
            Assert.Single(plan);                                                    // l'image importée n'est pas reconnue : gardée
            Assert.Equal(1, IconUpgrade.Apply(cfg, plan, IconUpgrade.ProgramOf));
            Assert.Equal("fedcba9876543210fedcba9876543210.png", old.ImagePath);
            Assert.Equal("0123456789abcdef0123456789abcdef.png", imported.ImagePath);
            Assert.Empty(IconUpgrade.Candidates(cfg, _ => 256, IconUpgrade.ProgramOf));   // une grande image n'est plus candidate
        });
    }
}

/// <summary>Rendu d'une touche avec une image en icône : la zone ne recouvre jamais le titre, l'image est décodée à la taille d'affichage.</summary>
[Collection("ui")]
public class TileIconLayoutTests
{
    readonly WpfHost _host;
    public TileIconLayoutTests(WpfHost host) => _host = host;

    static string StoreImage(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)(i % 251); pixels[i + 1] = 140; pixels[i + 2] = 40; pixels[i + 3] = 255; }
        var b = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        return IconStore.Store(b);
    }

    static Rect Bounds(FrameworkElement e, Visual root) => e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    public static IEnumerable<object[]> Cases()
    {
        foreach (var (w, h) in new[] { (512, 512), (16, 16), (300, 80) })
            foreach (var dpi in new[] { 1.0, 1.5, 2.0 })
                foreach (var mode in new[] { "stack", "top" })
                    yield return new object[] { w, h, dpi, mode };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheIconStaysInItsZoneAboveOrBelowATwoLineTitle(int imageWidth, int imageHeight, double dpi, string mode)
    {
        _host.Run(() =>
        {
            var name = StoreImage(imageWidth, imageHeight);
            var button = new DeckButton
            {
                Label = "Visual Studio Code Insiders édition", ImagePath = name, ImageFit = "contain", TitlePosition = mode == "stack" ? null : mode,
                Actions = { new DeckAction { Type = ActionTypes.Launch, Value = "code" } },
            };
            var face = TileFaceData.From(button);
            Assert.True(face.HasIconImage);
            Assert.False(face.HasImage);

            var tile = new Controls.TileFace { Width = 120, Height = 120, Face = face };
            var root = new Border { Child = tile };
            VisualTreeHelper.SetRootDpi(root, new DpiScale(dpi, dpi));
            root.Measure(new Size(120, 120));
            root.Arrange(new Rect(0, 0, 120, 120));
            root.UpdateLayout();
            tile.Face = null; tile.Face = face;   // dessin à l'échelle posée
            root.UpdateLayout();

            var image = WpfHost.Descendants<Image>(tile).Single(i => i.Name == "IconImage");
            var title = WpfHost.Descendants<TextBlock>(tile).Single(t => t.Name == "IconTitle");
            var ib = Bounds(image, root);
            var tb = Bounds(title, root);
            Assert.True(ib.Width > 4 && ib.Height > 4, $"icône vide {ib}");
            Assert.True(new Rect(0, 0, 120, 120).Contains(ib), $"icône {ib} hors de la touche");
            Assert.False(ib.IntersectsWith(tb) && Rect.Intersect(ib, tb).Height > 0.5, $"icône {ib} sur le titre {tb}");
            Assert.True(mode == "top" ? tb.Bottom <= ib.Top + 0.5 : tb.Top >= ib.Bottom - 0.5, $"ordre : icône {ib}, titre {tb}");
            Assert.True(title.ActualHeight > title.FontSize * 1.5, "le titre long tient sur deux lignes");

            var source = (BitmapSource)image.Source;
            int longest = Math.Max(imageWidth, imageHeight), decoded = Math.Max(source.PixelWidth, source.PixelHeight);
            if (longest >= 300) Assert.True(decoded < longest && decoded >= Math.Max(ib.Width, ib.Height) * dpi - 1, $"décodée en {decoded} px pour {Math.Max(ib.Width, ib.Height) * dpi:0} px affichés");
            else Assert.Equal(longest, decoded);   // jamais agrandie au décodage
            if (Math.Max(imageWidth, imageHeight) <= 16)
                Assert.Equal(BitmapScalingMode.NearestNeighbor, RenderOptions.GetBitmapScalingMode(image));          // multiple entier, sans flou
        });
    }

    [Fact]
    public void ASmallImageInFillModeIsShownAsAnIconAndABigOneStillFillsTheKey()
    {
        _host.Run(() =>
        {
            var small = TileFaceData.From(new DeckButton { Label = "A", ImagePath = StoreImage(32, 32) });
            Assert.True(small.HasIconImage);
            Assert.Equal(TileKind.Image, small.Kind);
            var big = TileFaceData.From(new DeckButton { Label = "B", ImagePath = StoreImage(400, 300) });
            Assert.True(big.HasImage);
            Assert.False(big.HasIconImage);
        });
    }

    [Fact]
    public void AMissingImageFallsBackToTheFluentIconOfTheAction()
    {
        _host.Run(() =>
        {
            var face = TileFaceData.From(new DeckButton { Label = "Site", ImagePath = "00000000000000000000000000000000.png", Actions = { new DeckAction { Type = ActionTypes.Url, Value = "https://example.org" } } });
            Assert.True(face.HasSymbol);
            Assert.Equal(Wpf.Ui.Controls.SymbolRegular.Globe24, face.Symbol);
        });
    }

    [Fact]
    public void ScreenshotOfIconTiles()
    {
        var path = _host.Run(() =>
        {
            var row = new WrapPanel { Background = Brushes.Black, Width = 760 };
            foreach (var (w, h, label) in new[] { (512, 512, "Grande image"), (16, 16, "Favicon 16 px"), (300, 80, "Image large et basse"), (256, 256, "Visual Studio Code Insiders édition") })
                foreach (var mode in new string?[] { null, "top" })
                    row.Children.Add(new Controls.TileFace
                    {
                        Width = 170, Height = 170, Margin = new Thickness(6),
                        Face = TileFaceData.From(new DeckButton { Label = label, ImagePath = StoreImage(w, h), ImageFit = "contain", TitlePosition = mode, Actions = { new DeckAction { Type = ActionTypes.Launch, Value = "x" } } }),
                    });
            var window = new Window { Content = row, SizeToContent = SizeToContent.WidthAndHeight };
            return _host.Render(window, "tile-icons", 760, 380);
        });
        Assert.True(File.Exists(path));
    }
}
