using System;
using System.Collections.Concurrent;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace TouchDeck.ViewModels;

/// <summary>Tout ce qu'il faut pour dessiner la face d'un bouton (deck, éditeur, aperçu) : même rendu partout.</summary>
public sealed class TileFaceData
{
    public string Label { get; init; } = "";
    public string Icon { get; init; } = "";
    public SymbolRegular Symbol { get; init; }
    public bool HasSymbol { get; init; }
    public bool HasIconText => !HasSymbol && Icon.Length > 0;
    public bool ShowTitle { get; init; } = true;
    /// <summary>"stack" (sous l'icône), "top", "middle" ou "bottom".</summary>
    public string TitleMode { get; init; } = "stack";
    public Brush Foreground { get; init; } = Brushes.White;
    public Brush Background { get; init; } = Brushes.Gray;
    public Brush? ImageBrush { get; init; }
    public bool IsFolder { get; init; }
    public bool IsBlank { get; init; }

    public bool HasImage => ImageBrush != null;
    public bool HasTitle => ShowTitle && Label.Length > 0;
    public bool HasVisual => HasImage || HasSymbol || HasIconText;

    public static readonly TileFaceData Empty = new() { IsBlank = true, Background = Brushes.Transparent };

    static readonly ConcurrentDictionary<(string, bool), Brush?> ImageBrushes = new();

    public static void ClearCache() => ImageBrushes.Clear();

    /// <summary>Même face avec un autre titre (titre dynamique, minuteur) : l'image et les couleurs sont réutilisées.</summary>
    public TileFaceData WithLabel(string label) => label == Label ? this : new TileFaceData
    {
        Label = label, Icon = Icon, Symbol = Symbol, HasSymbol = HasSymbol, ShowTitle = ShowTitle, TitleMode = TitleMode,
        Foreground = Foreground, Background = Background, ImageBrush = ImageBrush, IsFolder = IsFolder, IsBlank = IsBlank,
    };

    /// <param name="timer">Temps restant du minuteur du bouton, s'il tourne : remplace « {timer} », ou le titre s'il n'en contient pas.</param>
    public static TileFaceData From(DeckButton b, string? timer = null)
    {
        if (b.Blank) return Empty;
        bool hasSymbol = IconCatalog.TryResolve(b.Icon, out var symbol);
        bool contain = b.ImageFit == "contain";
        Brush? image = null;
        if (!string.IsNullOrEmpty(b.ImagePath))
        {
            image = ImageBrushes.GetOrAdd((b.ImagePath, contain), key =>
            {
                var src = IconStore.Load(key.Item1);
                if (src == null) return null;
                var brush = new ImageBrush(src) { Stretch = key.Item2 ? Stretch.Uniform : Stretch.UniformToFill };
                brush.Freeze();
                return brush;
            });
        }
        Brush bg;
        try { bg = Theme.TileGradient(b.Color); }
        catch { bg = Theme.B(Theme.SurfaceHi); }   // couleur absente ou invalide
        var fg = Theme.TryBrush(b.TextColor, out var fgBrush) ? fgBrush : Brushes.White;
        return new TileFaceData
        {
            Label = DisplayLabel(b, DateTime.Now, timer), Icon = b.Icon, Symbol = symbol, HasSymbol = hasSymbol,
            ShowTitle = !b.HideTitle, TitleMode = b.TitlePosition ?? "stack",
            Foreground = fg, Background = bg, ImageBrush = image, IsFolder = b.IsFolder,
        };
    }

    /// <summary>Titre affiché : modèles {time}/{date}/{timer} remplacés ; un minuteur en cours s'affiche même sans {timer}.</summary>
    public static string DisplayLabel(DeckButton b, DateTime now, string? timer)
    {
        if (timer != null && !b.Label.Contains("{timer}", StringComparison.OrdinalIgnoreCase)) return timer;
        return LabelTemplate.Render(b.Label, now, timer);
    }
}
