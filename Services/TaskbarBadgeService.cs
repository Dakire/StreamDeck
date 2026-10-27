using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using StreamDak.ViewModels;

namespace StreamDak;

/// <summary>
/// Pastille de la barre des tâches (<see cref="TaskbarItemInfo.Overlay"/>) : le total de notifications non lues de <see cref="MainViewModel.TotalUnread"/>,
/// plafonné « 99+ », retiré à zéro. L'icône est dessinée par <see cref="RenderTargetBitmap"/> aux couleurs du thème (ressource <c>DeckTileNotify</c>) et à la
/// densité de l'écran. Tout se passe sur le thread d'interface (le total n'est modifié que là).
/// </summary>
public sealed class TaskbarBadgeService : IDisposable
{
    /// <summary>Côté de la pastille en pixels indépendants de l'appareil (taille d'un overlay Windows : 16 sur 16).</summary>
    public const double IconSize = 16;

    Window? _window;
    MainViewModel? _vm;
    string? _text;          // texte actuellement posé, null = aucune pastille
    double _scale = 1;

    /// <summary>
    /// Texte de la pastille pour un total : null à zéro ou moins, « 7 », « 99 », « 99+ » (« 9+ » n'existe pas : au-delà de 99 on plafonne).
    /// Une valeur négative ou nulle ne produit jamais de pastille.
    /// </summary>
    public static string? Label(int total) => NotificationBadge.Text(total);

    /// <summary>Nom accessible de la pastille, lu par le lecteur d'écran sur le bouton de la barre des tâches.</summary>
    public static string Description(int total) => NotificationBadge.Text(total) is { } text ? NotificationBadge.Describe(text) : "";

    /// <summary>
    /// Associe le service à la fenêtre du deck et à son ViewModel (le dernier appel remplace le précédent ; une fenêtre fermée est abandonnée).
    /// À appeler sur le thread d'interface.
    /// </summary>
    public void Attach(Window window, MainViewModel vm)
    {
        Detach();
        _window = window;
        _vm = vm;
        _scale = Math.Max(1, VisualTreeHelper.GetDpi(window).DpiScaleX);
        vm.PropertyChanged += OnViewModelChanged;
        window.DpiChanged += OnDpiChanged;
        window.Closed += OnWindowClosed;
        Theme.Changed += OnThemeChanged;
        Update(force: true);
    }

    void Detach()
    {
        if (_vm != null) _vm.PropertyChanged -= OnViewModelChanged;
        if (_window != null)
        {
            _window.DpiChanged -= OnDpiChanged;
            _window.Closed -= OnWindowClosed;
        }
        Theme.Changed -= OnThemeChanged;
        _window = null;
        _vm = null;
        _text = null;
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.TotalUnread)) Update(force: false);
    }

    void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        _scale = Math.Max(1, e.NewDpi.DpiScaleX);
        Update(force: true);
    }

    void OnThemeChanged() => Update(force: true);

    void OnWindowClosed(object? sender, EventArgs e) => Detach();

    void Update(bool force)
    {
        if (_window == null || _vm == null) return;
        // Le thread d'interface seulement : un événement tardif d'un autre thread est replacé sur le dispatcher de la fenêtre
        if (!_window.Dispatcher.CheckAccess()) { _window.Dispatcher.BeginInvoke(() => Update(force)); return; }
        var text = Label(_vm.TotalUnread);
        if (!force && text == _text) return;
        _text = text;
        try
        {
            if (text == null)
            {
                if (_window.TaskbarItemInfo != null) { _window.TaskbarItemInfo.Overlay = null; _window.TaskbarItemInfo.Description = null; }
                return;
            }
            _window.TaskbarItemInfo ??= new TaskbarItemInfo();
            _window.TaskbarItemInfo.Overlay = Render(text, ThemeColor("DeckTileNotify", Color.FromRgb(0xD9, 0x2D, 0x20)), Colors.White, _scale);
            _window.TaskbarItemInfo.Description = Description(_vm.TotalUnread);
        }
        catch (Exception ex)
        {
            // Une pastille manquée ne doit jamais gêner le deck
            Log.Warn("Pastille de la barre des tâches impossible : " + ex.Message);
        }
    }

    static Color ThemeColor(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;

    /// <summary>Dessine une pastille ronde de <paramref name="text"/> (blanc sur fond de couleur) de 16 DIP à la densité <paramref name="scale"/> ; gelée.</summary>
    public static BitmapSource Render(string text, Color background, Color foreground, double scale)
    {
        scale = Math.Max(1, scale);
        int px = (int)Math.Ceiling(IconSize * scale);
        double dpi = 96 * scale;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(new SolidColorBrush(background), null, new Point(IconSize / 2, IconSize / 2), IconSize / 2, IconSize / 2);
            // « 99+ » tient dans 16 DIP en réduisant la police ; un chiffre ou deux restent lisibles
            double size = text.Length switch { 1 => 12, 2 => 10.5, _ => 8.5 };
            var typeface = new Typeface(new FontFamily("Segoe UI Variable, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size,
                new SolidColorBrush(foreground), dpi / 96);
            dc.DrawText(formatted, new Point((IconSize - formatted.Width) / 2, (IconSize - formatted.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(px, px, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Se détache de la fenêtre et du thème (libération du conteneur).</summary>
    public void Dispose() => Detach();
}
