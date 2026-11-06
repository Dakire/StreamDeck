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
public sealed class TileFaceData : IEquatable<TileFaceData>
{
    /// <summary>
    /// Égalité par contenu : une face recalculée à l'identique (horloge, valeur d'intégration inchangée) n'est pas republiée,
    /// donc <see cref="Controls.TileFace"/> ne se redessine pas. Les pinceaux de couleur unie se comparent par couleur, les autres par référence (ils sont mis en cache).
    /// </summary>
    public bool Equals(TileFaceData? other) =>
        other is not null && (ReferenceEquals(this, other) ||
        (Label == other.Label && Icon == other.Icon && Symbol == other.Symbol && HasSymbol == other.HasSymbol && ShowTitle == other.ShowTitle
        && TitleMode == other.TitleMode && IconImage == other.IconImage && IsFolder == other.IsFolder && IsBlank == other.IsBlank && Kind == other.Kind
        && Steps == other.Steps && Badge == other.Badge && IsDisabled == other.IsDisabled && State == other.State && Unavailable == other.Unavailable
        && Caption == other.Caption && Progress == other.Progress
        && SameBrush(Foreground, other.Foreground) && SameBrush(LabelBrush, other.LabelBrush) && SameBrush(Background, other.Background)
        && SameBrush(Border, other.Border) && SameBrush(Plate, other.Plate) && SameBrush(ImageBrush, other.ImageBrush)));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as TileFaceData);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Label, Icon, Kind, Steps, Badge, State, Progress, IconImage);

    static bool SameBrush(Brush? a, Brush? b) =>
        ReferenceEquals(a, b) || (a is SolidColorBrush x && b is SolidColorBrush y && x.Color == y.Color && x.Opacity == y.Opacity);

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
    /// <summary>Image en plein cadre (mode « Remplir » et image assez grande), ou null.</summary>
    public Brush? ImageBrush { get; init; }
    /// <summary>
    /// Image montrée comme une icône, dans une zone bornée qui laisse sa place au titre (nom dans <c>Icons\</c>) : icône d'application, favicon, image « entière »,
    /// ou image trop petite pour remplir la touche. Décodée par <see cref="Controls.TileFace"/> à la taille d'affichage réelle. Null sinon.
    /// </summary>
    public string? IconImage { get; init; }
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
    /// <summary>L'intégration visée par une étape est activée mais injoignable : anneau rouge en pointillés, touche atténuée, « indisponible » dans le nom accessible.</summary>
    public bool Unavailable { get; init; }
    /// <summary>Variante affichage : petite légende sous la valeur (nom du minuteur), sinon vide.</summary>
    public string Caption { get; init; } = "";
    /// <summary>Progression d'un minuteur en cours (0 à 1), sinon null.</summary>
    public double? Progress { get; init; }

    /// <summary>true si une image remplit la touche.</summary>
    public bool HasImage => ImageBrush != null;
    /// <summary>true si une image est montrée comme icône.</summary>
    public bool HasIconImage => IconImage != null;
    /// <summary>true si le titre est affiché.</summary>
    public bool HasTitle => ShowTitle && Label.Length > 0;
    /// <summary>true s'il y a quelque chose à afficher dans la pastille (image, icône ou texte).</summary>
    public bool HasVisual => HasImage || HasIconImage || HasSymbol || HasIconText;

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
    /// <param name="unavailable">L'intégration visée est injoignable.</param>
    public static TileFaceData From(DeckButton b, string? timer = null, double? progress = null, Func<string, string?>? values = null, bool? state = null, string? badge = null, bool unavailable = false)
    {
        if (b.Blank) return Empty;
        bool hasSymbol = IconCatalog.TryResolve(b.Icon, out var symbol);
        Brush? image = null;
        string? iconImage = null;
        if (!string.IsNullOrEmpty(b.ImagePath))
        {
            // « Remplir » seulement pour une image assez grande ; une image « entière » ou petite (icône de 32 px) est une icône, au-dessus du titre
            if (IconStore.Load(b.ImagePath) is System.Windows.Media.Imaging.BitmapSource src)
            {
                if (b.ImageFit != "contain" && Math.Max(src.PixelWidth, src.PixelHeight) >= IconLayout.MinFillPixels) image = FillBrush(b.ImagePath, src);
                else iconImage = b.ImagePath;
            }
            else if (!hasSymbol && FallbackSymbol(b) is { } fallback)
            {
                // Image introuvable (fichier effacé, extraction ratée) : icône Fluent du type d'action plutôt qu'une touche vide (l'échec est journalisé par IconStore)
                symbol = fallback;
                hasSymbol = true;
            }
        }
        var kind = KindOf(b, image != null || iconImage != null, timer != null);
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
            Label = DisplayLabel(b, DateTime.Now, timer, values), State = state, Unavailable = unavailable, Icon = b.Icon, Symbol = symbol, HasSymbol = hasSymbol,
            ShowTitle = !b.HideTitle, TitleMode = b.TitlePosition ?? "stack",
            Foreground = custom ? fgBrush : Brushes.White,
            LabelBrush = custom ? fgBrush : image != null ? Brushes.White : Theme.B(Theme.Current.Text),
            Background = body, Border = border, Plate = plate, ImageBrush = image, IconImage = iconImage, IsFolder = b.IsFolder,
            Kind = kind, Steps = b.IsFolder ? 0 : b.Actions.Count,
            IsDisabled = !b.IsFolder && b.Actions.Count == 0 && kind != TileKind.Display,
            Progress = progress, Badge = badge,
            Caption = timer != null ? b.Label.Replace("{timer}", "", StringComparison.OrdinalIgnoreCase).Trim() : "",
        };
    }

    /// <summary>Pinceau d'une image en plein cadre, gardé par nom d'image (une image absente n'est jamais gardée : restaurée, elle réapparaît).</summary>
    static Brush FillBrush(string name, ImageSource source)
    {
        if (ImageBrushes.TryGetValue((name, false), out var cached) && cached != null) return cached;
        var brush = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        ImageBrushes[(name, false)] = brush;
        return brush;
    }

    /// <summary>Icône Fluent cohérente avec la première étape (programme, lien, dossier…), affichée quand l'image du bouton est introuvable.</summary>
    public static SymbolRegular? FallbackSymbol(DeckButton b)
    {
        string name = b.IsFolder ? "Folder24" : b.Actions.FirstOrDefault()?.Type switch
        {
            ActionTypes.Launch or ActionTypes.Focus => "Apps24",
            ActionTypes.Url => "Globe24",
            _ => "Image24",
        };
        return IconCatalog.TryResolve(name, out var symbol) ? symbol : null;
    }

    /// <summary>Titre affiché : modèles {time}/{date}/{timer} remplacés ; un minuteur en cours s'affiche même sans {timer}.</summary>
    public static string DisplayLabel(DeckButton b, DateTime now, string? timer, Func<string, string?>? values = null)
    {
        if (timer != null && !b.Label.Contains("{timer}", StringComparison.OrdinalIgnoreCase)) return timer;
        return LabelTemplate.Render(b.Label, now, timer, values);
    }
}
