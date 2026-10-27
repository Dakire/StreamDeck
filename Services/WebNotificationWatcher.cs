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
/// (<c>DocumentTitleChanged</c>, fin de navigation, <c>FaviconChanged</c> pour une simple activité) ; pour un service décrit avec un sélecteur CSS, un
/// <c>MutationObserver</c> injecté dans la seule page cachée envoie le nombre par message web (validé strictement, ADR 0021 ; sans cet observateur, relecture toutes les
/// minutes). Les modes <see cref="BadgeMode.Title"/> et <see cref="BadgeMode.Off"/> n'injectent rien. Deck masqué, les pages sont mises en veille (<see cref="CoreWebView2.TrySuspendAsync"/>). Aucune fenêtre, aucun son, aucune autorisation, aucun téléchargement,
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
        public bool LastActivity { get; set; }
        public bool Reported { get; set; }
        public int? TitleCount { get; set; }
        public int? SelectorCount { get; set; }
        public bool Activity { get; set; }
        public bool FaviconChanged { get; set; }
        public int Candidates { get; set; } = -1;
        public bool ObserverInstalled { get; set; }
        public FaviconActivity Favicon { get; } = new();
        public Action? Detach { get; set; }
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

    /// <inheritdoc/>
    public event Action<string, bool>? ActivityChanged;

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
        return _providers.All.Where(p => p.Mode != BadgeMode.Off && (_only.Count == 0 || _only.Contains(p.Id)) && urls.Any(p.Matches)).ToList();
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
            session.ObserverInstalled = await InstallObserverAsync(session, controller.CoreWebView2);
            if (session.Closed || generation != session.Generation) return;
            Log.Info($"Notifications : suivi de {session.Provider.Name} ({session.Provider.StartUrl.Host}, mode {session.Provider.Mode}{(session.ObserverInstalled ? ", observateur" : "")})");
            controller.CoreWebView2.Navigate(session.Provider.StartUrl.AbsoluteUri);
            if (_background) _ = ApplyBackgroundAsync(session);
            if (session.Provider.ScriptInterval is { } interval && !session.ObserverInstalled)
            {
                session.Timer = new DispatcherTimer(interval, DispatcherPriority.Background, (_, _) => _ = EvaluateAsync(session), _ui);
                if (_background) session.Timer.Stop();
            }
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Log.Warn($"Notifications : {session.Provider.Name} indisponible ({ex.Message})");
            if (!session.Reported) Report(session, new BadgeReading(null, false));      // sinon on garde la dernière valeur connue
        }
    }

    /// <summary>
    /// Injecte l'observateur du sélecteur dans cette seule page cachée si le mode le demande. Les messages web ne sont activés que sur ce contrôleur ; le cadre visible
    /// d'une page web du deck garde <c>IsWebMessageEnabled = false</c>. Rend false (relecture périodique) si l'injection échoue.
    /// </summary>
    static async Task<bool> InstallObserverAsync(Session session, CoreWebView2 core)
    {
        if (session.Provider.ObserverScript is not { } script) return false;
        try
        {
            core.Settings.IsWebMessageEnabled = true;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or ObjectDisposedException)
        {
            Log.Warn($"Notifications : observateur de {session.Provider.Name} indisponible ({ex.Message})");
            try { core.Settings.IsWebMessageEnabled = false; } catch (Exception inner) when (inner is COMException or InvalidOperationException or ObjectDisposedException) { }
            return false;
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
        // Gestionnaires nommés pour pouvoir s'en désabonner à la fermeture (aucune fuite si la page est rouverte).
        EventHandler<CoreWebView2NewWindowRequestedEventArgs> newWindow = (_, a) => a.Handled = true;
        EventHandler<CoreWebView2DownloadStartingEventArgs> download = (_, a) => a.Cancel = true;
        // Permissions toujours refusées : ni micro, ni caméra, ni notifications système (donc pas de NotificationReceived) ; le badge suffit.
        EventHandler<CoreWebView2PermissionRequestedEventArgs> permission = (_, a) => a.State = CoreWebView2PermissionState.Deny;
        EventHandler<CoreWebView2NavigationStartingEventArgs> navigation = (_, a) => { if (!WidgetTypes.IsWebAddress(a.Uri, out Uri _)) a.Cancel = true; };
        EventHandler<object> title = (_, _) => _ = EvaluateAsync(session);
        EventHandler<CoreWebView2NavigationCompletedEventArgs> completed = (_, _) => _ = EvaluateAsync(session);
        EventHandler<CoreWebView2SourceChangedEventArgs> source = (_, _) => _ = EvaluateAsync(session);
        EventHandler<object> favicon = (_, _) => OnFaviconChanged(session, core);
        EventHandler<CoreWebView2WebMessageReceivedEventArgs> message = (_, a) => OnWebMessage(session, a);
        EventHandler<CoreWebView2ProcessFailedEventArgs> failed = (_, a) =>
        {
            Log.Warn($"Notifications : la page de {session.Provider.Name} a cessé de répondre ({a.ProcessFailedKind})");   // la dernière valeur connue reste affichée
            _ = ReopenLaterAsync(session);
        };
        core.NewWindowRequested += newWindow;
        core.DownloadStarting += download;
        core.PermissionRequested += permission;
        core.NavigationStarting += navigation;
        core.DocumentTitleChanged += title;
        core.NavigationCompleted += completed;
        core.SourceChanged += source;
        core.FaviconChanged += favicon;
        core.ProcessFailed += failed;
        if (session.Provider.ObserverScript != null) core.WebMessageReceived += message;
        session.Detach = () =>
        {
            try
            {
                core.NewWindowRequested -= newWindow;
                core.DownloadStarting -= download;
                core.PermissionRequested -= permission;
                core.NavigationStarting -= navigation;
                core.DocumentTitleChanged -= title;
                core.NavigationCompleted -= completed;
                core.SourceChanged -= source;
                core.FaviconChanged -= favicon;
                core.ProcessFailed -= failed;
                core.WebMessageReceived -= message;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException) { /* déjà libérée */ }
        };
    }

    /// <summary>
    /// Message web de la page cachée. Tout est ignoré sauf <c>{"type":"streamdak.badge","count":N}</c> (N entier de 0 à 999 999) envoyé par un document de l'hôte du
    /// service, quand le mode du service demande l'observateur (<see cref="NotificationMessage"/>).
    /// </summary>
    void OnWebMessage(Session session, CoreWebView2WebMessageReceivedEventArgs a)
    {
        if (session.Closed) return;
        try
        {
            if (!NotificationMessage.TryRead(session.Provider, a.Source, a.WebMessageAsJson, out var count, out var candidates)) return;
            session.SelectorCount = count;
            session.Candidates = candidates;
            LogSelector(session, count, candidates);
            Recompute(session);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException or ArgumentException) { /* message illisible : ignoré */ }
    }

    /// <summary>
    /// Le favicon change. Il n'affiche un point d'activité que si l'utilisateur l'a demandé (<see cref="INotificationProvider.ShowFaviconActivity"/>, faux par défaut :
    /// la première icône vue n'est pas toujours « sans notification »). Sinon il ne sert qu'au journal, pour comprendre un nombre non lu.
    /// </summary>
    void OnFaviconChanged(Session session, CoreWebView2 core)
    {
        if (session.Closed || session.Provider.Mode is BadgeMode.Off or BadgeMode.Title) return;
        try
        {
            session.FaviconChanged = session.Favicon.Observe(core.FaviconUri);
            session.Activity = session.Provider.Mode == BadgeMode.Auto && session.Provider.ShowFaviconActivity && session.FaviconChanged;
            WarnIfSilent(session);
            Recompute(session);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException) { /* page libérée */ }
    }

    async Task ReopenLaterAsync(Session session)
    {
        int generation = session.Generation;
        await Task.Delay(RetryDelay);
        await _ui.InvokeAsync(() =>
        {
            if (session.Closed || generation != session.Generation || !_sessions.ContainsKey(session.Provider.Id)) return;
            session.Detach?.Invoke();
            session.Detach = null;
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
            // En mode « sélecteur », le titre ne sert qu'à savoir qu'on est bien sur une page du service.
            int? count = page == null ? null
                : session.Provider.Mode == BadgeMode.Selector ? (session.Provider.Matches(page) ? 0 : null)
                : session.Provider.CountFromTitle(page, core.DocumentTitle ?? "");
            session.TitleCount = count;
            if (count == null) { session.SelectorCount = null; session.Activity = false; session.Favicon.Reset(); }   // hors du service : plus rien de lu
            else if (page != null && session.Provider.CountScript is { } script && session.Provider.Matches(page) && !session.ObserverInstalled)
            {
                var raw = await core.ExecuteScriptAsync(script);
                if (!session.Closed && TryNumber(raw, out var scripted)) session.SelectorCount = scripted;
            }
            var reading = NotificationBadge.Resolve(session.Provider.Mode, session.TitleCount, session.SelectorCount, session.Activity);
            LogReading(session, page, core.DocumentTitle, reading.Count);
            Report(session, reading);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException or JsonException)
        {
            Log.Warn($"Notifications : lecture de {session.Provider.Name} impossible ({ex.Message})");
        }
    }

    /// <summary>Dernière lecture journalisée par service : on ne consigne que les changements.</summary>
    readonly Dictionary<string, string> _logged = new();

    /// <summary>
    /// Journalise ce que la page affiche (hôte, titre « masqué », résultat) pour comprendre un badge absent : les lettres du titre sont remplacées par « · » (noms, adresses,
    /// contenu de messages ne sont jamais écrits), les chiffres et la ponctuation restent, ce qui montre le format du compteur.
    /// </summary>
    void LogReading(Session session, Uri? page, string? title, int? count)
    {
        var masked = new string((title ?? "").Select(c => char.IsLetter(c) ? '·' : c).ToArray());
        var line = $"{page?.Host ?? "?"} · titre « {(masked.Length > 60 ? masked[..60] + "…" : masked)} » · compteur {(count?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "inconnu")}";
        if (_logged.TryGetValue(session.Provider.Id, out var before) && before == line) return;
        _logged[session.Provider.Id] = line;
        Log.Info($"Notifications : {session.Provider.Name} : {line}");
    }

    /// <summary>
    /// Diagnostic du sélecteur ou des libellés : mode, candidats trouvés, nombre retenu. Jamais de texte de la page. Une ligne seulement quand la lecture change.
    /// </summary>
    void LogSelector(Session session, int count, int candidates)
    {
        var how = session.Provider is NotificationProvider { Selector: null, UnreadLabels: true } ? "libellés non lus" : "sélecteur";
        var line = $"{how} · candidats {(candidates < 0 ? "?" : candidates.ToString(System.Globalization.CultureInfo.InvariantCulture))} · retenu {count}";
        if (_loggedSelector.TryGetValue(session.Provider.Id, out var before) && before == line) { WarnIfSilent(session); return; }
        _loggedSelector[session.Provider.Id] = line;
        Log.Info($"Notifications : {session.Provider.Name} : mode {session.Provider.Mode} · {line}");
        WarnIfSilent(session);
    }

    readonly Dictionary<string, string> _loggedSelector = new();
    readonly HashSet<string> _warnedSilent = new();

    /// <summary>
    /// Message clair, une fois par service, quand l'icône de l'onglet a changé alors que le sélecteur ou les libellés ne trouvent rien : le réglage
    /// (<c>selector</c>, <c>labelPattern</c> de <c>notifications.json</c>) ne correspond pas à la page.
    /// </summary>
    void WarnIfSilent(Session session)
    {
        if (!session.FaviconChanged || session.SelectorCount is not 0 || session.Candidates != 0 || !session.ObserverInstalled) return;
        if (!_warnedSilent.Add(session.Provider.Id)) return;
        Log.Warn($"Notifications : {session.Provider.Name} : l'icône de l'onglet signale une activité mais aucun élément de la page ne porte un nombre de non lus (0 candidat). " +
                 "Ajuste « selector » / « attribute » ou « labelPattern » dans la section « badges » de notifications.json.");
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

    /// <summary>Recalcule la lecture du service d'après ses sources (titre, sélecteur, activité) et la publie si elle a changé.</summary>
    void Recompute(Session session) =>
        Report(session, NotificationBadge.Resolve(session.Provider.Mode, session.TitleCount, session.SelectorCount, session.Activity));

    /// <summary>Publie le nombre et l'activité s'ils ont changé ; toujours sur le thread d'interface.</summary>
    void Report(Session session, BadgeReading reading)
    {
        if (!_ui.CheckAccess()) { _ui.InvokeAsync(() => Report(session, reading)); return; }
        if (!session.Reported || session.Last != reading.Count)
        {
            session.Reported = true;
            session.Last = reading.Count;
            CountChanged?.Invoke(session.Provider.Id, reading.Count);
        }
        if (session.LastActivity != reading.Activity)
        {
            session.LastActivity = reading.Activity;
            ActivityChanged?.Invoke(session.Provider.Id, reading.Activity);
        }
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
        session.Detach?.Invoke();
        session.Detach = null;
        try { session.Controller?.Close(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException) { /* déjà fermée */ }
        session.Controller = null;
        if (report)
        {
            session.Last = null;
            CountChanged?.Invoke(session.Provider.Id, null);
            if (session.LastActivity) { session.LastActivity = false; ActivityChanged?.Invoke(session.Provider.Id, false); }
        }
    }

    /// <summary>Arrête le suivi.</summary>
    public void Dispose()
    {
        if (_ui.CheckAccess()) StopCore();
        else _ui.InvokeAsync(StopCore);
    }
}
