using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TouchDeck.ViewModels;

namespace TouchDeck.Controls;

/// <summary>Dessine la face d'un bouton à partir d'un <see cref="TileFaceData"/>, avec des tailles proportionnelles à la case.</summary>
public partial class TileFace : UserControl
{
    public static readonly DependencyProperty FaceProperty = DependencyProperty.Register(
        nameof(Face), typeof(TileFaceData), typeof(TileFace), new PropertyMetadata(null, (d, _) => ((TileFace)d).Render()));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(TileFace), new PropertyMetadata(new CornerRadius(12), (d, _) => ((TileFace)d).Render()));

    public TileFaceData? Face { get => (TileFaceData?)GetValue(FaceProperty); set => SetValue(FaceProperty, value); }
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    public TileFace()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Render();
    }

    void Render()
    {
        var f = Face;
        if (f == null || f.IsBlank)
        {
            ImageLayer.Visibility = Scrim.Visibility = StackLayer.Visibility = OverlayLayer.Visibility = FolderBadge.Visibility = Visibility.Collapsed;
            return;
        }
        double s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0) s = 100;

        ImageLayer.CornerRadius = CornerRadius;
        ImageLayer.Background = f.ImageBrush;
        ImageLayer.Visibility = f.HasImage ? Visibility.Visible : Visibility.Collapsed;

        bool hasTitle = f.HasTitle;
        // Avec une image, le titre est toujours superposé (en bas par défaut) et l'icône disparaît
        string mode = f.TitleMode;
        if (f.HasImage && mode == "stack") mode = "bottom";
        bool stack = mode == "stack";

        Scrim.Visibility = f.HasImage && hasTitle && mode == "bottom" ? Visibility.Visible : Visibility.Collapsed;
        Scrim.CornerRadius = new CornerRadius(0, 0, CornerRadius.BottomRight, CornerRadius.BottomLeft);
        Scrim.Height = s * 0.45;

        double titleSize = Math.Clamp(s * 0.1, 10, 40);
        double iconSize = s * (hasTitle && stack ? 0.36 : 0.5);
        bool showIcon = !f.HasImage && f.HasVisual;
        var margin = new Thickness(Math.Max(4, s * 0.06), Math.Max(4, s * 0.06), Math.Max(4, s * 0.06), Math.Max(4, s * 0.06));

        StackLayer.Visibility = stack ? Visibility.Visible : Visibility.Collapsed;
        OverlayLayer.Visibility = stack ? Visibility.Collapsed : Visibility.Visible;
        StackLayer.MaxWidth = Math.Max(10, ActualWidth - 2 * margin.Left);

        Fill(StackSymbol, StackIconText, StackTitle, f, showIcon, hasTitle && stack, iconSize, titleSize);
        StackTitle.Margin = new Thickness(0, s * 0.03, 0, 0);
        Fill(OverlaySymbol, OverlayIconText, OverlayTitle, f, showIcon && !stack, hasTitle && !stack, iconSize, titleSize);
        OverlayTitle.Margin = margin;
        OverlayTitle.MaxWidth = Math.Max(10, ActualWidth - 2 * margin.Left);
        OverlayTitle.VerticalAlignment = mode switch { "top" => VerticalAlignment.Top, "middle" => VerticalAlignment.Center, _ => VerticalAlignment.Bottom };
        OverlayTitle.MaxHeight = Math.Max(10, ActualHeight - 2 * margin.Top);
        if (f.HasImage && hasTitle) OverlayTitle.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.9 };
        else OverlayTitle.Effect = null;

        double badge = Math.Clamp(s * 0.2, 14, 30);
        FolderBadge.Visibility = f.IsFolder ? Visibility.Visible : Visibility.Collapsed;
        FolderBadge.Width = FolderBadge.Height = badge;
        FolderBadge.Margin = new Thickness(0, 0, s * 0.06, s * 0.06);
        FolderGlyph.FontSize = badge * 0.7;
    }

    static void Fill(Wpf.Ui.Controls.SymbolIcon symbol, TextBlock iconText, TextBlock title, TileFaceData f,
        bool showIcon, bool showTitle, double iconSize, double titleSize)
    {
        symbol.Symbol = f.Symbol;
        symbol.Foreground = f.Foreground;
        symbol.FontSize = iconSize;
        symbol.Visibility = showIcon && f.HasSymbol ? Visibility.Visible : Visibility.Collapsed;

        iconText.Text = f.Icon;
        iconText.Foreground = f.Foreground;
        iconText.FontSize = iconSize;
        iconText.Visibility = showIcon && f.HasIconText ? Visibility.Visible : Visibility.Collapsed;

        title.Text = f.Label;
        title.Foreground = f.Foreground;
        title.FontSize = titleSize;
        title.Visibility = showTitle ? Visibility.Visible : Visibility.Collapsed;
    }
}
