using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace StreamDak;

/// <summary>
/// Suit le nombre de notifications non lues des services dont un bouton porte l'adresse, dans des pages WebView2 cachées : une par service, avec le profil de
/// navigation de StreamDak (la session ouverte dans une page web du deck sert aussi ici ; aucun identifiant n'est lu ni enregistré). Le compteur se lit sur événement
/// (<c>DocumentTitleChanged</c>, fin de navigation) ; un script en lecture seule ne tourne que pour les services décrits avec un sélecteur CSS, et seulement toutes les
/// minutes. Deck masqué, les pages sont mises en veille (<see cref="CoreWebView2.TrySuspendAsync"/>). Aucune fenêtre, aucun son, aucune autorisation, aucun téléchargement,
/// aucun objet de l'application exposé à la page. Tous les appels passent par le thread d'interface.
/// </summary>
public sealed class WebNotificationWatcher : INotificationWatcher, IDisposable
{
    /// <summary>Après un plantage de la page, nouvelle tentative au bout de ce délai.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    sealed class Session
    {
        public required INotificationProvider Provider { get; init; }
        public CoreWebView2Controller? Controller { get; set; }
        public DispatcherTimer? Timer { get; set; }
        public int? Last { get; set; }
        public bool Reported { get; set; }
        public bool Closed { get; set; }
        public bool Suspended { get; set; }
        public int Generation { get; set; }
    }

    readonly DeckDocument _doc;
    readonly NotificationProviders _providers;
    readonly Dispatcher _ui;
    readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    IReadOnlySet<string> _only = new HashSet<string>();
    HwndSource? _host;
    bool _running;
    bool _background;

    /// <summary>Suivi des services de <paramref name="providers"/> dont une adresse figure dans <paramref name="doc"/>.</summary>
    public WebNotificationWatcher(DeckDocument doc, NotificationProviders providers)
    {
        _doc = doc;
        _providers = providers;
        _ui = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    /// <inheritdoc/>
    public event Action<string, int?>? CountChanged;

    /// <summary>Identifiants des services actuellement suivis (tests).</summary>
    public IReadOnlyCollection<string> Watched => _sessions.Keys.ToList();

    /// <inheritdoc/>
    public void Start(IReadOnlySet<string> only)
    {
        _ui.InvokeAsync(() =>
        {
            _only = only;
            if (!_running) { _running = true; _doc.Changed += OnDocumentChanged; }
            Sync();
        });
    }

    /// <inheritdoc/>
    public void Stop() => _ui.InvokeAsync(StopCore);

    /// <summary>Même effet que <see cref="Stop"/>, rendu quand tout est libéré (tests et arrêt de l'application).</summary>
    public Task StopAsync() => _ui.InvokeAsync(StopCore).Task;

    void StopCore()
    {
        if (_running) { _doc.Changed -= OnDocumentChanged; _running = false; }
        foreach (var session in _sessions.Values.ToList()) Close(session, report: true);
        _sessions.Clear();
        _host?.Dispose();
        _host = null;
    }

    /// <inheritdoc/>
    public void Refresh() => _ui.InvokeAsync(() => { foreach (var s in _sessions.Values.ToList()) _ = EvaluateAsync(s); });

    /// <inheritdoc/>
    public void SetBackground(bool background) => _ui.InvokeAsync(() =>
    {
        _background = background;
        foreach (var s in _sessions.Values.ToList()) _ = ApplyBackgroundAsync(s);
    });

    void OnDocumentChanged(DocumentChange change)
    {
        if (change.Kind != ChangeKind.Settings) _ui.InvokeAsync(Sync, DispatcherPriority.Background);   // une adresse de bouton a pu changer
    }

    /// <summary>Services à suivre : ceux qu'une adresse de la configuration désigne, limités au choix de l'utilisateur.</summary>
    List<INotificationProvider> Wanted()
    {
        var urls = NotificationBadge.Urls(_doc.Config).ToList();
        return _providers.All.Where(p => (_only.Count == 0 || _only.Contains(p.Id)) && urls.Any(p.Matches)).ToList();
    }

    void Sync()
    {
        if (!_running) return;
        var wanted = Wanted();
        foreach (var gone in _sessions.Values.Where(s => wanted.All(p => p.Id != s.Provider.Id)).ToList())
        {
            Close(gone, report: true);
            _sessions.Remove(gone.Provider.Id);
        }
        foreach (var provider in wanted)
            if (!_sessions.ContainsKey(provider.Id))
            {
                var session = new Session { Provider = provider };
                _sessions[provider.Id] = session;
                _ = OpenAsync(session);
            }
    }

    /// <summary>Fenêtre jamais affichée, ni dans la barre des tâches ni dans Alt+Tab : le support obligatoire des pages cachées.</summary>
    HwndSource Host() => _host ??= new HwndSource(new HwndSourceParameters("StreamDak.Notifications")
    {
        Width = 1, Height = 1, PositionX = -32000, PositionY = -32000,
        WindowStyle = unchecked((int)0x80000000),            // WS_POPUP, sans WS_VISIBLE
        ExtendedWindowStyle = 0x00000080 | 0x08000000,       // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
    });

    async Task OpenAsync(Session session)
    {
        int generation = ++session.Generation;
        try
        {
            var environment = await Controls.WebWidgetView.GetEnvironment();
            if (session.Closed || generation != session.Generation) return;
            var controller = await environment.CreateCoreWebView2ControllerAsync(Host().Handle);
            if (session.Closed || generation != session.Generation) { controller.Close(); return; }
            session.Controller = controller;
            controller.IsVisible = false;
            controller.Bounds = new System.Drawing.Rectangle(0, 0, 1100, 800);   // une taille de fenêtre normale : certaines pages n'affichent leur compteur que dans une mise en page large
            Configure(session, controller.CoreWebView2);
            Log.Info($"Notifications : suivi de {session.Provider.Name} ({session.Provider.StartUrl.Host})");
            controller.CoreWebView2.Navigate(session.Provider.StartUrl.AbsoluteUri);
            if (_background) _ = ApplyBackgroundAsync(session);
            if (session.Provider.ScriptInterval is { } interval)
            {
                session.Timer = new DispatcherTimer(interval, DispatcherPriority.Background, (_, _) => _ = EvaluateAsync(session), _ui);
                if (_background) session.Timer.Stop();
            }
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Log.Warn($"Notifications : {session.Provider.Name} indisponible ({ex.Message})");
            Report(session, null);
        }
    }

    void Configure(Session session, CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        core.IsMuted = true;                                           // une page cachée ne fait aucun bruit
        core.NewWindowRequested += (_, a) => a.Handled = true;
        core.DownloadStarting += (_, a) => a.Cancel = true;
        core.PermissionRequested += (_, a) => a.State = CoreWebView2PermissionState.Deny;   // ni micro, ni caméra, ni notifications système : le badge suffit
        core.NavigationStarting += (_, a) => { if (!WidgetTypes.IsWebAddress(a.Uri, out Uri _)) a.Cancel = true; };
        core.DocumentTitleChanged += (_, _) => _ = EvaluateAsync(session);
        core.NavigationCompleted += (_, _) => _ = EvaluateAsync(session);
        core.SourceChanged += (_, _) => _ = EvaluateAsync(session);
        core.ProcessFailed += (_, a) =>
        {
            Log.Warn($"Notifications : la page de {session.Provider.Name} a cessé de répondre ({a.ProcessFailedKind})");
            Report(session, null);
            _ = ReopenLaterAsync(session);
        };
    }

    async Task ReopenLaterAsync(Session session)
    {
        int generation = session.Generation;
        await Task.Delay(RetryDelay);
        await _ui.InvokeAsync(() =>
        {
            if (session.Closed || generation != session.Generation || !_sessions.ContainsKey(session.Provider.Id)) return;
            session.Controller?.Close();
            session.Controller = null;
            _ = OpenAsync(session);
        });
    }

    /// <summary>Lit le compteur : titre de la page, puis script facultatif ; publie seulement s'il change.</summary>
    async Task EvaluateAsync(Session session)
    {
        if (session.Closed || session.Suspended || session.Controller?.CoreWebView2 is not { } core) return;
        try
        {
            Uri.TryCreate(core.Source, UriKind.Absolute, out var page);
            int? count = page == null ? null : session.Provider.CountFromTitle(page, core.DocumentTitle ?? "");
            if (count != null && page != null && session.Provider.CountScript is { } script && session.Provider.Matches(page))
            {
                var raw = await core.ExecuteScriptAsync(script);
                if (!session.Closed && TryNumber(raw, out var scripted)) count = scripted;
            }
            Report(session, count);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException or JsonException)
        {
            Log.Warn($"Notifications : lecture de {session.Provider.Name} impossible ({ex.Message})");
        }
    }

    /// <summary>Nombre d'un résultat de script (JSON : « 7 », « null »).</summary>
    public static bool TryNumber(string? json, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(json) || json == "null") return false;
        if (!double.TryParse(json, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d)) return false;
        value = (int)Math.Clamp(Math.Round(d), 0, 999_999);
        return true;
    }

    void Report(Session session, int? count)
    {
        if (session.Reported && session.Last == count) return;
        session.Reported = true;
        session.Last = count;
        CountChanged?.Invoke(session.Provider.Id, count);
    }

    async Task ApplyBackgroundAsync(Session session)
    {
        if (session.Closed || session.Controller?.CoreWebView2 is not { } core) return;
        try
        {
            if (_background)
            {
                session.Timer?.Stop();
                session.Suspended = await core.TrySuspendAsync();   // la page dort : plus de minuteries ni de réseau pour un deck que personne ne voit
            }
            else
            {
                core.Resume();
                session.Suspended = false;
                session.Timer?.Start();
                await EvaluateAsync(session);           // les titres ont pu changer pendant le sommeil
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            Log.Warn($"Notifications : mise en veille de {session.Provider.Name} impossible ({ex.Message})");
        }
    }

    void Close(Session session, bool report)
    {
        session.Closed = true;
        session.Timer?.Stop();
        session.Timer = null;
        try { session.Controller?.Close(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException) { /* déjà fermée */ }
        session.Controller = null;
        if (report) { session.Last = null; CountChanged?.Invoke(session.Provider.Id, null); }
    }

    /// <summary>Arrête le suivi.</summary>
    public void Dispose()
    {
        if (_ui.CheckAccess()) StopCore();
        else _ui.InvokeAsync(StopCore);
    }
}
