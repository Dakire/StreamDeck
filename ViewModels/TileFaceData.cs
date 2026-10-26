using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace StreamDak.ViewModels;

/// <summary>
/// Variantes de touche, toutes dessinées par le même composant (<see cref="Controls.TileFace"/>) :
/// standard (pastille carrée), média et audio (pastille ronde), navigation (pastille en contour, chevron),
/// affichage (horloge, minuteur : la valeur en grand), image (plein cadre).
/// </summary>
public enum TileKind
{
    /// <summary>Pastille carrée et titre.</summary>
    Standard,
    /// <summary>Touche média ou audio : pastille ronde.</summary>
    Media,
    /// <summary>Dossier, page ou profil : pastille en contour.</summary>
    Navigation,
    /// <summary>Horloge ou minuteur : la valeur en grand.</summary>
    Display,
    /// <summary>Image importée en plein cadre.</summary>
    Image,
}

/// <summary>Tout ce qu'il faut pour dessiner la face d'un bouton (deck, éditeur, aperçu) : même rendu partout.</summary>
public sealed class TileFaceData
{
    /// <summary>Titre affiché (modèles déjà remplacés).</summary>
    public string Label { get; init; } = "";
    /// <summary>Icône enregistrée (nom Fluent, emoji ou texte).</summary>
    public string Icon { get; init; } = "";
    /// <summary>Icône Fluent résolue, si <see cref="HasSymbol"/>.</summary>
    public SymbolRegular Symbol { get; init; }
    /// <summary>true si l'icône est une icône Fluent connue.</summary>
    public bool HasSymbol { get; init; }
    /// <summary>true si l'icône est un emoji ou un texte court.</summary>
    public bool HasIconText => !HasSymbol && Icon.Length > 0;
    /// <summary>false si le titre est masqué.</summary>
    public bool ShowTitle { get; init; } = true;
    /// <summary>"stack" (sous l'icône), "top", "middle" ou "bottom".</summary>
    public string TitleMode { get; init; } = "stack";
    /// <summary>Couleur de l'icône (sur la pastille).</summary>
    public Brush Foreground { get; init; } = Brushes.White;
    /// <summary>Couleur du titre (sur le corps de la touche).</summary>
    public Brush LabelBrush { get; init; } = Brushes.White;
    /// <summary>Corps de la touche : surface teintée par la couleur du bouton.</summary>
    public Brush Background { get; init; } = Brushes.Gray;
    /// <summary>Liseré de la touche.</summary>
    public Brush Border { get; init; } = Brushes.Transparent;
    /// <summary>Pastille d'icône : la couleur du bouton, pleine.</summary>
    public Brush Plate { get; init; } = Brushes.Gray;
    /// <summary>Image importée, ou null.</summary>
    public Brush? ImageBrush { get; init; }
    /// <summary>true pour un dossier (badge chevron).</summary>
    public bool IsFolder { get; init; }
    /// <summary>true pour une case vide.</summary>
    public bool IsBlank { get; init; }
    /// <summary>Variante de dessin.</summary>
    public TileKind Kind { get; init; }
    /// <summary>Nombre d'étapes (badge affiché à partir de 2).</summary>
    public int Steps { get; init; }
    /// <summary>Texte du badge rouge de notifications (« 3 », « 99+ »), ou null.</summary>
    public string? Badge { get; init; }
    /// <summary>Touche sans action : affichée atténuée.</summary>
    public bool IsDisabled { get; init; }
    /// <summary>Voyant d'une commande d'intégration : vrai (actif, rond plein), faux (inactif, rond vide), null (aucun voyant).</summary>
    public bool? State { get; init; }
    /// <summary>Variante affichage : petite légende sous la valeur (nom du minuteur), sinon vide.</summary>
    public string Caption { get; init; } = "";
    /// <summary>Progression d'un minuteur en cours (0 à 1), sinon null.</summary>
    public double? Progress { get; init; }

    /// <summary>true si une image est affichée.</summary>
    public bool HasImage => ImageBrush != null;
    /// <summary>true si le titre est affiché.</summary>
    public bool HasTitle => ShowTitle && Label.Length > 0;
    /// <summary>true s'il y a quelque chose à afficher dans la pastille (image, icône ou texte).</summary>
    public bool HasVisual => HasImage || HasSymbol || HasIconText;

    /// <summary>Case vide.</summary>
    public static readonly TileFaceData Empty = new() { IsBlank = true, Background = Brushes.Transparent };

    static readonly ConcurrentDictionary<(string, bool), Brush?> ImageBrushes = new();

    /// <summary>Oublie les images chargées (thème changé, image remplacée).</summary>
    public static void ClearCache() => ImageBrushes.Clear();

    /// <summary>Variante d'après le contenu : rien n'est stocké dans la configuration.</summary>
    public static TileKind KindOf(DeckButton b, bool hasImage, bool counting)
    {
        if (hasImage) return TileKind.Image;
        if (counting || (b.Actions.Count == 0 && LabelTemplate.IsDynamic(b.Label))) return TileKind.Display;
        if (b.IsFolder || (b.Actions.Count > 0 && b.Actions.All(a => a.Type is ActionTypes.Page or ActionTypes.Profile))) return TileKind.Navigation;
        if (b.Actions.Count > 0 && b.Actions[0].Type is ActionTypes.Media or ActionTypes.Audio) return TileKind.Media;
        return TileKind.Standard;
    }

    /// <summary>Face d'un bouton de la configuration, d'après son contenu (variante, couleurs, image, titre dynamique).</summary>
    /// <param name="b">Bouton à dessiner.</param>
    /// <param name="timer">Temps restant du minuteur du bouton, s'il tourne : remplace « {timer} », ou le titre s'il n'en contient pas.</param>
    /// <param name="progress">Part écoulée de ce minuteur (0 à 1).</param>
    /// <param name="values">Valeur d'état d'une intégration d'après sa clé (« obs.scene »), pour les titres dynamiques.</param>
    /// <param name="state">Voyant d'une commande d'intégration (actif, inactif), ou null s'il n'y en a pas.</param>
    /// <param name="badge">Texte du badge rouge de notifications (« 3 », « 99+ »), ou null.</param>
    public static TileFaceData From(DeckButton b, string? timer = null, double? progress = null, Func<string, string?>? values = null, bool? state = null, string? badge = null)
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
            Label = DisplayLabel(b, DateTime.Now, timer, values), State = state, Icon = b.Icon, Symbol = symbol, HasSymbol = hasSymbol,
            ShowTitle = !b.HideTitle, TitleMode = b.TitlePosition ?? "stack",
            Foreground = custom ? fgBrush : Brushes.White,
            LabelBrush = custom ? fgBrush : image != null ? Brushes.White : Theme.B(Theme.Current.Text),
            Background = body, Border = border, Plate = plate, ImageBrush = image, IsFolder = b.IsFolder,
            Kind = kind, Steps = b.IsFolder ? 0 : b.Actions.Count,
            IsDisabled = !b.IsFolder && b.Actions.Count == 0 && kind != TileKind.Display,
            Progress = progress, Badge = badge,
            Caption = timer != null ? b.Label.Replace("{timer}", "", StringComparison.OrdinalIgnoreCase).Trim() : "",
        };
    }

    /// <summary>Titre affiché : modèles {time}/{date}/{timer} remplacés ; un minuteur en cours s'affiche même sans {timer}.</summary>
    public static string DisplayLabel(DeckButton b, DateTime now, string? timer, Func<string, string?>? values = null)
    {
        if (timer != null && !b.Label.Contains("{timer}", StringComparison.OrdinalIgnoreCase)) return timer;
        return LabelTemplate.Render(b.Label, now, timer, values);
    }
}
