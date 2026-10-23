using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StreamDak.ViewModels;

namespace StreamDak.Controls;

/// <summary>
/// Page web du widget ou de la page web plein cadre, dans un cadre WebView2 isolé : profil de navigation propre à StreamDak (jamais celui d'Edge),
/// aucun objet de l'application exposé à la page, pas de message vers l'application, pas de fenêtre ni de téléchargement, navigation limitée à
/// http et https. Caméra, micro et notifications ne sont accordés qu'aux pages https du même site que l'adresse saisie (ADR 0018) ; tout autre
/// droit (position…) est refusé. La page ne peut rien faire sur StreamDak : elle s'affiche, c'est tout (ADR 0017).
/// Le cadre est rangé (<see cref="WebFramePool"/>) quand la tuile quitte l'écran et repris au retour : la page n'est pas rechargée.
/// </summary>
public partial class WebWidgetView : UserControl
{
    static Task<CoreWebView2Environment>? _environment;

    /// <summary>Intervalle de la détection d'un champ de texte, et nombre de relevés sans champ avant de rendre le clavier (≈ 1,2 s).</summary>
    static readonly TimeSpan FieldPoll = TimeSpan.FromMilliseconds(400);
    const int FieldIdleTicks = 3;

    /// <summary>true si l'élément qui a le focus dans la page (iframes de même origine comprises) accepte du texte.</summary>
    const string FocusedFieldScript =
        "(function(){var e=document.activeElement;for(var i=0;i<4&&e&&e.tagName==='IFRAME';i++){try{e=e.contentDocument.activeElement}catch(x){return false}}" +
        "if(!e)return false;var t=e.tagName;if(t==='TEXTAREA')return !e.readOnly;" +
        "if(t==='INPUT'){var k=(e.type||'text').toLowerCase();return !e.readOnly&&['button','submit','reset','checkbox','radio','range','color','file','image','hidden'].indexOf(k)<0}" +
        "return !!e.isContentEditable})()";

    WebWidgetViewModel? _vm;
    WebFrame? _frame;
    string? _key;                 // clé du cadre affiché (celle de la tuile au moment de l'affichage)
    readonly DispatcherTimer _fieldTimer = new() { Interval = FieldPoll };
    bool _polling;
    int _idleTicks;
    int _readyTicks;              // relevés écoulés depuis que le cadre est prêt : le zoom enregistré a eu le temps d'être appliqué

    /// <summary>Vue d'un widget page web ; le cadre est repris ou créé quand la vue est chargée.</summary>
    public WebWidgetView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _fieldTimer.Tick += async (_, _) => { SyncZoom(); await PollFocusedFieldAsync(); };
        DataContextChanged += OnDataContextChanged;
    }

    WebView2? Web => _frame?.Web;

    void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        bool shown = _frame != null;
        if (shown) Unmount();   // la liste réutilise la vue pour une autre tuile : son cadre est rangé, celui de la nouvelle tuile est repris
        Attach();
        if (shown && IsLoaded) Mount();
    }

    void Attach()
    {
        if (_vm != null) _vm.PropertyChanged -= OnViewModelChanged;
        _vm = DataContext as WebWidgetViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnViewModelChanged;
            _vm.ReloadRequested = () => Web?.CoreWebView2?.Reload();
        }
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // La saisie au clavier commence : le focus passe à la page (la fenêtre vient de devenir activable)
        if (e.PropertyName == nameof(WebWidgetViewModel.ZoomPercent) && Web != null && _vm != null) Web.ZoomFactor = _vm.ZoomPercent / 100.0;
        if (e.PropertyName == nameof(WebWidgetViewModel.IsTyping) && _vm is { IsTyping: true })
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (Web?.CoreWebView2 != null) Web.Focus(); });
    }

    /// <summary>Profil de navigation propre à StreamDak, créé une fois : le dossier de configuration (ou STREAMDAK_DATA_DIR) contient WebView2.</summary>
    static Task<CoreWebView2Environment> GetEnvironment() =>
        _environment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(ConfigStore.Dir, "WebView2"));

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_frame != null) return;
        Attach();
        Mount();
    }

    /// <summary>Affiche le cadre de la tuile : celui qui était rangé (page déjà chargée), sinon un nouveau qui charge la page.</summary>
    async void Mount()
    {
        if (_vm is not { Source: { } source } vm) return;
        _key = vm.FrameKey;
        var frame = _key != null ? WebFramePool.Take(_key) : null;
        bool fresh = frame == null;
        frame ??= new WebFrame { Web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent }, Source = source };
        _frame = frame;
        frame.Vm = vm;
        frame.Web.SetBinding(VisibilityProperty, new Binding(nameof(WebWidgetViewModel.ShowPage)) { Source = vm, Converter = new BooleanToVisibilityConverter() });
        WebHost.Children.Add(frame.Web);
        frame.Web.ZoomFactor = vm.ZoomPercent / 100.0;
        frame.Web.ZoomFactorChanged += OnZoomFactorChanged;
        _readyTicks = fresh ? 0 : FieldIdleTicks;
        _fieldTimer.Start();
        if (!fresh) return;   // page déjà chargée : rien d'autre à faire

        try
        {
            await frame.Web.EnsureCoreWebView2Async(await GetEnvironment());
            Configure(frame);
            Log.Info($"Widget page web : WebView2 {frame.Web.CoreWebView2.Environment.BrowserVersionString} prêt, ouverture de {source.Host}");
            frame.Web.CoreWebView2.Navigate(source.AbsoluteUri);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Log.Warn($"Widget page web : WebView2 indisponible ({ex.Message})");
            frame.Vm?.Fail("Le composant WebView2 de Microsoft est introuvable ou ne démarre pas : installe-le depuis microsoft.com/edge/webview2.");
        }
    }

    /// <summary>Réglages et garde-fous du cadre ; les événements passent par <see cref="WebFrame.Vm"/> (le cadre survit à la vue).</summary>
    static void Configure(WebFrame frame)
    {
        var core = frame.Web.CoreWebView2;
        var settings = core.Settings;
        settings.AreHostObjectsAllowed = false;             // aucun objet de l'application visible depuis la page
        settings.IsWebMessageEnabled = false;               // aucun message page → application
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        core.NewWindowRequested += (_, a) =>
        {
            // Jamais de fenêtre ni d'onglet : un lien « nouvel onglet » (target="_blank", window.open) s'ouvre dans le cadre lui-même.
            // Sans cela le bouton « Sign in » de Google Agenda, qui ouvre un nouvel onglet, ne faisait rien.
            a.Handled = true;
            if (WidgetTypes.IsWebAddress(a.Uri, out var target)) core.Navigate(target.AbsoluteUri);
        };
        core.PermissionRequested += (_, a) => OnPermissionRequested(frame, a);
        core.DownloadStarting += (_, a) => a.Cancel = true;                                      // ni téléchargement
        core.NavigationStarting += (sender, a) =>
        {
            bool allowed = WidgetTypes.IsWebAddress(a.Uri, out _);
            Log.Info($"Widget page web : navigation vers {HostOf(a.Uri)} {(allowed ? "" : "REFUSÉE (ni http ni https)")}");
            if (!allowed) a.Cancel = true;
            else if (EmbeddedBrowserRefusal.IsRefusal(a.Uri))
            {
                a.Cancel = true;
                frame.Vm?.Fail(EmbeddedBrowserRefusal.Message);
            }
        };
        core.ProcessFailed += (_, _) => frame.Vm?.Fail("La page a cessé de répondre : touche « Recharger ».");
        core.NavigationCompleted += (_, a) =>
        {
            Log.Info($"Widget page web : chargement terminé succès={a.IsSuccess} erreur={a.WebErrorStatus} http={a.HttpStatusCode}");
            if (a.IsSuccess) frame.Vm?.Recover();
            else if (a.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled) frame.Vm?.Fail("La page n'a pas pu être chargée : vérifie l'adresse et la connexion.");
        };
    }

    /// <summary>Notifications, micro et caméra : accordés aux pages https du même site que l'adresse saisie (ADR 0018). Tout le reste est refusé.</summary>
    static void OnPermissionRequested(WebFrame frame, CoreWebView2PermissionRequestedEventArgs a)
    {
        bool device = a.PermissionKind is CoreWebView2PermissionKind.Notifications or CoreWebView2PermissionKind.Microphone or CoreWebView2PermissionKind.Camera;
        bool allowed = device && Uri.TryCreate(a.Uri, UriKind.Absolute, out var requester) && WebPermissions.MayUseDevices(requester, frame.Source);
        a.State = allowed ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
        Log.Info($"Widget page web : autorisation {a.PermissionKind} demandée par {HostOf(a.Uri)} : {(allowed ? "accordée" : "refusée")}");
    }

    /// <summary>
    /// Filet de l'événement ci-dessous : le zoom du cadre (pincement du doigt, Ctrl + molette) est relu à chaque relevé. Seulement une fois le cadre
    /// prêt depuis un moment, pour ne jamais écraser le zoom enregistré par la valeur d'un cadre qui s'initialise encore.
    /// </summary>
    void SyncZoom()
    {
        if (Web is not { CoreWebView2: not null } web || _vm == null) return;
        if (_readyTicks < FieldIdleTicks) { _readyTicks++; return; }
        int percent = (int)Math.Round(web.ZoomFactor * 100);
        if (percent != _vm.ZoomPercent) _vm.SetZoom(percent);
    }

    /// <summary>Zoom changé dans la page elle-même (pincement du doigt, Ctrl + molette) : le modèle le retient et le deck l'enregistre.</summary>
    void OnZoomFactorChanged(object? sender, EventArgs e)
    {
        if (Web is { } web) _vm?.SetZoom((int)Math.Round(web.ZoomFactor * 100));
    }

    /// <summary>Nom d'hôte d'une adresse pour le journal (jamais le chemin ni les paramètres : ils peuvent porter des jetons).</summary>
    static string HostOf(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var u) ? (u.Host.Length > 0 ? u.Host : u.Scheme + ":") : "?";

    /// <summary>
    /// Saisie automatique : la fenêtre du deck ne prend jamais le clavier, donc taper dans une page demandait de toucher d'abord le bouton clavier.
    /// Dès qu'un champ de texte de la page a le focus, la saisie démarre ; elle s'arrête d'elle-même ~1,2 s après la sortie du champ.
    /// Un script en lecture seule est exécuté par l'application dans la page (aucun objet ni message de la page vers l'application).
    /// </summary>
    async Task PollFocusedFieldAsync()
    {
        if (_polling || _vm is not { ShowPage: true } vm || Web?.CoreWebView2 is not { } core) return;
        _polling = true;
        try
        {
            bool inField = await core.ExecuteScriptAsync(FocusedFieldScript) == "true";
            if (inField)
            {
                _idleTicks = 0;
                if (!vm.IsTyping) { vm.AutoTyping = true; vm.TypingRequested?.Invoke(true); }
            }
            else if (vm is { IsTyping: true, AutoTyping: true } && ++_idleTicks >= FieldIdleTicks)
            {
                _idleTicks = 0;
                vm.AutoTyping = false;
                vm.TypingRequested?.Invoke(false);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ObjectDisposedException) { /* page en cours de navigation ou cadre fermé */ }
        finally { _polling = false; }
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnViewModelChanged;
        Unmount();
    }

    /// <summary>Retire le cadre de la vue : rangé avec sa page si la tuile a une clé, libéré sinon.</summary>
    void Unmount()
    {
        _fieldTimer.Stop();
        if (_vm != null) _vm.ReloadRequested = null;
        if (_frame is not { } frame) return;
        frame.Web.ZoomFactorChanged -= OnZoomFactorChanged;
        WebHost.Children.Remove(frame.Web);
        BindingOperations.ClearBinding(frame.Web, VisibilityProperty);
        frame.Vm = null;
        _frame = null;
        if (_key != null) WebFramePool.Park(_key, frame);
        else WebFramePool.Release(frame);
        _key = null;
    }
}
