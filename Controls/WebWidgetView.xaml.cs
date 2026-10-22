using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using TouchDeck.ViewModels;

namespace TouchDeck.Controls;

/// <summary>
/// Page web du widget, dans un cadre WebView2 isolé : profil de navigation propre à TouchDeck (jamais celui d'Edge), aucun objet de l'application
/// exposé à la page, pas de message vers l'application, pas de fenêtre, de téléchargement ni d'autorisation (caméra, micro, position,
/// notifications), navigation limitée à http et https. La page ne peut rien faire sur TouchDeck : elle s'affiche, c'est tout (ADR 0017).
/// </summary>
public partial class WebWidgetView : UserControl
{
    static Task<CoreWebView2Environment>? _environment;

    WebWidgetViewModel? _vm;
    bool _started;

    /// <summary>Vue d'un widget page web ; le cadre est créé quand la vue est chargée.</summary>
    public WebWidgetView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) => Attach();
    }

    void Attach()
    {
        if (_vm != null) _vm.PropertyChanged -= OnViewModelChanged;
        _vm = DataContext as WebWidgetViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnViewModelChanged;
            _vm.ReloadRequested = () => Web.CoreWebView2?.Reload();
        }
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // La saisie au clavier commence : le focus passe à la page (la fenêtre vient de devenir activable)
        if (e.PropertyName == nameof(WebWidgetViewModel.IsTyping) && _vm is { IsTyping: true })
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (Web.CoreWebView2 != null) Web.Focus(); });
    }

    /// <summary>Profil de navigation propre à TouchDeck, créé une fois : le dossier de configuration (ou TOUCHDECK_DATA_DIR) contient WebView2.</summary>
    static Task<CoreWebView2Environment> GetEnvironment() =>
        _environment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(ConfigStore.Dir, "WebView2"));

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;
        Attach();
        if (_vm is not { Source: { } source }) return;
        try
        {
            await Web.EnsureCoreWebView2Async(await GetEnvironment());
            Configure(Web.CoreWebView2);
            Web.CoreWebView2.Navigate(source.AbsoluteUri);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Widget page web : WebView2 indisponible ({ex.Message})");
            _vm.Fail("Le composant WebView2 de Microsoft est introuvable ou ne démarre pas : installe-le depuis microsoft.com/edge/webview2.");
        }
    }

    void Configure(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.AreHostObjectsAllowed = false;             // aucun objet de l'application visible depuis la page
        settings.IsWebMessageEnabled = false;               // aucun message page → application
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        core.NewWindowRequested += (_, a) => a.Handled = true;                                   // pas de fenêtre
        core.PermissionRequested += (_, a) => a.State = CoreWebView2PermissionState.Deny;        // ni caméra, ni micro, ni position, ni notifications
        core.DownloadStarting += (_, a) => a.Cancel = true;                                      // ni téléchargement
        core.NavigationStarting += (sender, a) => { if (!WidgetTypes.IsWebAddress(a.Uri, out _)) a.Cancel = true; };
        core.ProcessFailed += (_, _) => _vm?.Fail("La page a cessé de répondre : touche « Recharger ».");
        core.NavigationCompleted += (_, a) =>
        {
            if (a.IsSuccess) _vm?.Recover();
            else if (a.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled) _vm?.Fail("La page n'a pas pu être chargée : vérifie l'adresse et la connexion.");
        };
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm != null) { _vm.PropertyChanged -= OnViewModelChanged; _vm.ReloadRequested = null; }
        Web.Dispose();   // libère les processus du navigateur de ce cadre
    }
}
