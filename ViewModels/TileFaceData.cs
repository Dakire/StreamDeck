using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace TouchDeck.ViewModels;

/// <summary>
/// Variantes de touche, toutes dessinées par le même composant (<see cref="Controls.TileFace"/>) :
/// standard (pastille carrée), média et audio (pastille ronde), navigation (pastille en contour, chevron),
/// affichage (horloge, minuteur : la valeur en grand), image (plein cadre).
/// </summary>
public enum TileKind { Standard, Media, Navigation, Display, Image }

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
    /// <summary>Couleur de l'icône (sur la pastille).</summary>
    public Brush Foreground { get; init; } = Brushes.White;
    /// <summary>Couleur du titre (sur le corps de la touche).</summary>
    public Brush LabelBrush { get; init; } = Brushes.White;
    /// <summary>Corps de la touche : surface teintée par la couleur du bouton.</summary>
    public Brush Background { get; init; } = Brushes.Gray;
    public Brush Border { get; init; } = Brushes.Transparent;
    /// <summary>Pastille d'icône : la couleur du bouton, pleine.</summary>
    public Brush Plate { get; init; } = Brushes.Gray;
    public Brush? ImageBrush { get; init; }
    public bool IsFolder { get; init; }
    public bool IsBlank { get; init; }
    public TileKind Kind { get; init; }
    /// <summary>Nombre d'étapes (badge affiché à partir de 2).</summary>
    public int Steps { get; init; }
    /// <summary>Touche sans action : affichée atténuée.</summary>
    public bool IsDisabled { get; init; }
    /// <summary>Variante affichage : petite légende sous la valeur (nom du minuteur), sinon vide.</summary>
    public string Caption { get; init; } = "";
    /// <summary>Progression d'un minuteur en cours (0 à 1), sinon null.</summary>
    public double? Progress { get; init; }

    public bool HasImage => ImageBrush != null;
    public bool HasTitle => ShowTitle && Label.Length > 0;
    public bool HasVisual => HasImage || HasSymbol || HasIconText;

    public static readonly TileFaceData Empty = new() { IsBlank = true, Background = Brushes.Transparent };

    static readonly ConcurrentDictionary<(string, bool), Brush?> ImageBrushes = new();

    public static void ClearCache() => ImageBrushes.Clear();

    /// <summary>Même face avec un autre titre (titre dynamique, minuteur) : l'image et les couleurs sont réutilisées.</summary>
    public TileFaceData WithLabel(string label) => label == Label ? this : Copy(label, Progress);

    TileFaceData Copy(string label, double? progress) => new()
    {
        Label = label, Icon = Icon, Symbol = Symbol, HasSymbol = HasSymbol, ShowTitle = ShowTitle, TitleMode = TitleMode,
        Foreground = Foreground, LabelBrush = LabelBrush, Background = Background, Border = Border, Plate = Plate,
        ImageBrush = ImageBrush, IsFolder = IsFolder, IsBlank = IsBlank, Kind = Kind, Steps = Steps, IsDisabled = IsDisabled,
        Progress = progress, Caption = Caption,
    };

    /// <summary>Variante d'après le contenu : rien n'est stocké dans la configuration.</summary>
    public static TileKind KindOf(DeckButton b, bool hasImage, bool counting)
    {
        if (hasImage) return TileKind.Image;
        if (counting || (b.Actions.Count == 0 && LabelTemplate.IsDynamic(b.Label))) return TileKind.Display;
        if (b.IsFolder || (b.Actions.Count > 0 && b.Actions.All(a => a.Type is "page" or "profile"))) return TileKind.Navigation;
        if (b.Actions.Count > 0 && b.Actions[0].Type is "media" or "audio") return TileKind.Media;
        return TileKind.Standard;
    }

    /// <param name="timer">Temps restant du minuteur du bouton, s'il tourne : remplace « {timer} », ou le titre s'il n'en contient pas.</param>
    /// <param name="progress">Part écoulée de ce minuteur (0 à 1).</param>
    public static TileFaceData From(DeckButton b, string? timer = null, double? progress = null)
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
                // Réduction de qualité : une image de 512 px affichée en 200 px ne doit pas créneler
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
                brush.Freeze();
                return brush;
            });
        }
        var kind = KindOf(b, image != null, timer != null);
        Brush body, border, plate;
        try
        {
            body = Theme.TileFill(b.Color, kind == TileKind.Navigation ? 0.55 : 1);
            border = Theme.TileBorder(b.Color);
            plate = Theme.TileGradient(b.Color);
        }
        catch
        {
            // Couleur absente ou invalide : touche neutre
            body = Theme.B(Theme.SurfaceHi); border = Theme.B(Theme.Current.Line); plate = Theme.B(Theme.Current.Grip);
        }
        bool custom = Theme.TryBrush(b.TextColor, out var fgBrush);
        return new TileFaceData
        {
            Label = DisplayLabel(b, DateTime.Now, timer), Icon = b.Icon, Symbol = symbol, HasSymbol = hasSymbol,
            ShowTitle = !b.HideTitle, TitleMode = b.TitlePosition ?? "stack",
            Foreground = custom ? fgBrush : Brushes.White,
            LabelBrush = custom ? fgBrush : image != null ? Brushes.White : Theme.B(Theme.Current.Text),
            Background = body, Border = border, Plate = plate, ImageBrush = image, IsFolder = b.IsFolder,
            Kind = kind, Steps = b.IsFolder ? 0 : b.Actions.Count,
            IsDisabled = !b.IsFolder && b.Actions.Count == 0 && kind != TileKind.Display,
            Progress = progress,
            Caption = timer != null ? b.Label.Replace("{timer}", "", StringComparison.OrdinalIgnoreCase).Trim() : "",
        };
    }

    /// <summary>Titre affiché : modèles {time}/{date}/{timer} remplacés ; un minuteur en cours s'affiche même sans {timer}.</summary>
    public static string DisplayLabel(DeckButton b, DateTime now, string? timer)
    {
        if (timer != null && !b.Label.Contains("{timer}", StringComparison.OrdinalIgnoreCase)) return timer;
        return LabelTemplate.Render(b.Label, now, timer);
    }
}
