using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StreamDak.ViewModels;

namespace StreamDak.Controls;

/// <summary>Dessine la face d'un bouton à partir d'un <see cref="TileFaceData"/>, avec des tailles proportionnelles à la case.</summary>
public partial class TileFace : UserControl
{
    /// <summary>Propriété de dépendance de <see cref="Face"/>.</summary>
    public static readonly DependencyProperty FaceProperty = DependencyProperty.Register(
        nameof(Face), typeof(TileFaceData), typeof(TileFace), new PropertyMetadata(null, (d, _) => ((TileFace)d).Render()));

    /// <summary>Propriété de dépendance de <see cref="CornerRadius"/>.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(TileFace), new PropertyMetadata(new CornerRadius(12), (d, _) => ((TileFace)d).Render()));

    /// <summary>Contenu à dessiner ; null ou vide : rien n'est affiché.</summary>
    public TileFaceData? Face { get => (TileFaceData?)GetValue(FaceProperty); set => SetValue(FaceProperty, value); }
    /// <summary>Rayon des coins (celui de la touche), repris par l'image et le dégradé du titre.</summary>
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    static readonly Brush DimRing = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)));
    static readonly Brush DimLight = Freeze(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)));

    /// <summary>Ombre du titre posé sur une image : partagée et figée plutôt que recréée à chaque dessin de la face.</summary>
    static readonly System.Windows.Media.Effects.Effect TitleShadow = FreezeEffect(new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.9 });

    static System.Windows.Media.Effects.Effect FreezeEffect(System.Windows.Media.Effects.Effect e) { e.Freeze(); return e; }

    /// <summary>Redessine la face à chaque changement de taille : toutes les dimensions sont proportionnelles à la case.</summary>
    public TileFace()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Render();
    }

    static Brush Freeze(Brush b) { b.Freeze(); return b; }

    void Render()
    {
        var f = Face;
        if (f == null || f.IsBlank)
        {
            ImageLayer.Visibility = Scrim.Visibility = FaceContent.Visibility = FolderBadge.Visibility =
                StepBadge.Visibility = ProgressTrack.Visibility = Visibility.Collapsed;
            return;
        }
        FaceContent.Visibility = Visibility.Visible;
        double w = ActualWidth > 0 ? ActualWidth : 100, h = ActualHeight > 0 ? ActualHeight : 100;
        double s = Math.Min(w, h);
        double pad = Math.Max(4, s * 0.07);

        ImageLayer.CornerRadius = CornerRadius;
        ImageLayer.Background = f.ImageBrush;
        ImageLayer.Visibility = f.HasImage ? Visibility.Visible : Visibility.Collapsed;
        FaceContent.Opacity = f.IsDisabled ? 0.45 : 1;

        bool hasTitle = f.HasTitle;
        // Avec une image, le titre est toujours superposé (en bas par défaut) et l'icône disparaît
        string mode = f.TitleMode;
        if (f.HasImage && mode == "stack") mode = "bottom";
        bool display = f.Kind == TileKind.Display;
        bool stack = mode == "stack" && !display;
        bool overlay = !stack && !display;

        Scrim.Visibility = f.HasImage && hasTitle && mode == "bottom" ? Visibility.Visible : Visibility.Collapsed;
        Scrim.CornerRadius = new CornerRadius(0, 0, CornerRadius.BottomRight, CornerRadius.BottomLeft);
        Scrim.Height = h * 0.5;

        double titleSize = Math.Clamp(s * 0.095, 10, 40);
        StackLayer.Visibility = stack ? Visibility.Visible : Visibility.Collapsed;
        DisplayLayer.Visibility = display ? Visibility.Visible : Visibility.Collapsed;
        OverlayLayer.Visibility = overlay ? Visibility.Visible : Visibility.Collapsed;

        if (stack) RenderStack(f, s, w, pad, titleSize, hasTitle);
        if (display) RenderDisplay(f, s, w, pad, titleSize);
        if (overlay) RenderOverlay(f, s, w, h, pad, titleSize, hasTitle, mode);

        // Badges : discrets, dans les coins, de même taille
        double badge = Math.Clamp(s * 0.17, 14, 30);
        var badgeRadius = new CornerRadius(badge / 2);
        FolderBadge.Visibility = f.IsFolder ? Visibility.Visible : Visibility.Collapsed;
        FolderBadge.Width = FolderBadge.Height = badge;
        FolderBadge.CornerRadius = badgeRadius;
        FolderBadge.Margin = new Thickness(0, 0, pad, pad);
        FolderGlyph.FontSize = badge * 0.7;

        // Badge de notifications : rond rouge au coin haut droit, plus large pour « 99+ » ; le badge d'étapes lui laisse la place
        bool notify = f.Badge != null;
        NotifyBadge.Visibility = notify ? Visibility.Visible : Visibility.Collapsed;
        double notifyHeight = Math.Max(badge * 1.15, 18);
        NotifyBadge.Height = notifyHeight;
        NotifyBadge.MinWidth = notifyHeight;
        NotifyBadge.CornerRadius = new CornerRadius(notifyHeight / 2);
        NotifyBadge.Padding = new Thickness(notifyHeight * 0.25, 0, notifyHeight * 0.25, 0);
        NotifyBadge.Margin = new Thickness(0, pad * 0.6, pad * 0.6, 0);
        NotifyText.FontSize = notifyHeight * 0.55;
        NotifyText.Text = f.Badge ?? "";
        NotifyBadge.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, notify ? f.Badge + " notifications" : string.Empty);

        StepBadge.Visibility = f.Steps > 1 ? Visibility.Visible : Visibility.Collapsed;
        StepBadge.Height = badge;
        StepBadge.CornerRadius = badgeRadius;
        StepBadge.Padding = new Thickness(badge * 0.3, 0, badge * 0.35, 0);
        StepBadge.Margin = new Thickness(0, pad, notify ? pad * 1.6 + notifyHeight * (f.Badge!.Length > 2 ? 1.6 : 1.0) : pad, 0);
        StepGlyph.FontSize = badge * 0.6;
        StepText.FontSize = badge * 0.55;
        StepText.Text = f.Steps.ToString(CultureInfo.CurrentCulture);

        // Voyant d'état : plein et vert quand la commande est active, simple anneau quand elle ne l'est pas
        StateLed.Visibility = f.State == null ? Visibility.Collapsed : Visibility.Visible;
        if (f.State is { } on)
        {
            StateLed.Width = StateLed.Height = Math.Max(10, badge * 0.6);
            StateLed.Margin = new Thickness(pad, pad, 0, 0);
            var color = TryFindResource("DeckSuccess") as Brush ?? Brushes.LimeGreen;
            StateLed.Stroke = on ? color : DimRing;
            StateLed.Fill = on ? color : Brushes.Transparent;
        }

        // Barre de progression du minuteur, posée au bas de la touche
        ProgressTrack.Visibility = f.Progress != null ? Visibility.Visible : Visibility.Collapsed;
        if (f.Progress is { } p)
        {
            double barH = Math.Max(3, s * 0.03);
            ProgressTrack.Height = barH;
            ProgressTrack.CornerRadius = ProgressFill.CornerRadius = new CornerRadius(barH / 2);
            ProgressTrack.Margin = new Thickness(pad * 1.4, 0, pad * 1.4, pad);
            ProgressFill.Width = Math.Max(0, (w - pad * 2.8) * (1 - p));   // la barre se vide avec le temps restant
            ProgressFill.Background = f.LabelBrush;
        }
    }

    void RenderStack(TileFaceData f, double s, double w, double pad, double titleSize, bool hasTitle)
    {
        bool showIcon = f.HasVisual;
        double plate = s * (hasTitle ? 0.4 : 0.5);
        Plate.Visibility = showIcon ? Visibility.Visible : Visibility.Collapsed;
        Plate.Width = Plate.Height = plate;
        // Média : pastille ronde ; navigation : pastille en contour (on lit tout de suite qu'elle mène ailleurs)
        Plate.CornerRadius = new CornerRadius(f.Kind == TileKind.Media ? plate / 2 : plate * 0.28);
        bool outline = f.Kind == TileKind.Navigation;
        Plate.Background = outline ? DimLight : f.Plate;
        Plate.BorderBrush = outline ? f.Plate : null;
        Plate.BorderThickness = new Thickness(outline ? Math.Max(2, plate * 0.045) : 0);

        double icon = plate * 0.56;
        StackSymbol.Symbol = f.Symbol;
        StackSymbol.Foreground = f.Foreground;
        StackSymbol.FontSize = icon;
        StackSymbol.Visibility = f.HasSymbol ? Visibility.Visible : Visibility.Collapsed;
        StackIconText.Text = f.Icon;
        StackIconText.Foreground = f.Foreground;
        StackIconText.FontSize = f.Icon.Length <= 2 ? icon : icon * 2.2 / Math.Max(2, f.Icon.Length);
        StackIconText.Visibility = f.HasIconText ? Visibility.Visible : Visibility.Collapsed;

        StackTitle.Text = f.Label;
        StackTitle.Foreground = f.LabelBrush;
        StackTitle.FontSize = titleSize;
        StackTitle.MaxHeight = titleSize * 2.7;    // deux lignes au plus
        StackTitle.Margin = new Thickness(0, showIcon ? s * 0.07 : 0, 0, 0);
        StackTitle.Visibility = hasTitle ? Visibility.Visible : Visibility.Collapsed;
        StackLayer.MaxWidth = Math.Max(10, w - 2 * pad);
    }

    void RenderDisplay(TileFaceData f, double s, double w, double pad, double titleSize)
    {
        DisplaySymbol.Symbol = f.Symbol;
        DisplaySymbol.Foreground = f.LabelBrush;
        DisplaySymbol.FontSize = s * 0.14;
        DisplaySymbol.Visibility = f.HasSymbol ? Visibility.Visible : Visibility.Collapsed;
        DisplaySymbol.Margin = new Thickness(0, 0, 0, s * 0.03);

        // La valeur occupe la largeur disponible : « 18:37 » en très grand, une date longue plus petite
        double avail = w - 2 * pad;
        double fit = avail / Math.Max(3, f.Label.Length * 0.58);
        DisplayValue.Text = f.Label;
        DisplayValue.Foreground = f.LabelBrush;
        DisplayValue.FontSize = Math.Clamp(Math.Min(s * 0.28, fit), 10, 140);
        DisplayValue.MaxWidth = Math.Max(10, avail);

        DisplayCaption.Text = f.Caption;
        DisplayCaption.Foreground = f.LabelBrush;
        DisplayCaption.FontSize = titleSize * 0.9;
        DisplayCaption.MaxWidth = Math.Max(10, avail);
        DisplayCaption.Visibility = f.Caption.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void RenderOverlay(TileFaceData f, double s, double w, double h, double pad, double titleSize, bool hasTitle, string mode)
    {
        bool showIcon = !f.HasImage && f.HasVisual;
        double icon = s * 0.42;
        OverlaySymbol.Symbol = f.Symbol;
        OverlaySymbol.Foreground = f.Foreground == Brushes.White ? f.LabelBrush : f.Foreground;
        OverlaySymbol.FontSize = icon;
        OverlaySymbol.Visibility = showIcon && f.HasSymbol ? Visibility.Visible : Visibility.Collapsed;
        OverlayIconText.Text = f.Icon;
        OverlayIconText.Foreground = OverlaySymbol.Foreground;
        OverlayIconText.FontSize = icon;
        OverlayIconText.Visibility = showIcon && f.HasIconText ? Visibility.Visible : Visibility.Collapsed;

        OverlayTitle.Text = f.Label;
        OverlayTitle.Foreground = f.LabelBrush;
        OverlayTitle.FontSize = titleSize;
        OverlayTitle.Visibility = hasTitle ? Visibility.Visible : Visibility.Collapsed;
        OverlayTitle.Margin = new Thickness(pad);
        OverlayTitle.MaxWidth = Math.Max(10, w - 2 * pad);
        OverlayTitle.MaxHeight = Math.Max(10, h - 2 * pad);
        OverlayTitle.VerticalAlignment = mode switch { "top" => VerticalAlignment.Top, "middle" => VerticalAlignment.Center, _ => VerticalAlignment.Bottom };
        OverlayTitle.Effect = f.HasImage && hasTitle ? TitleShadow : null;
    }
}
