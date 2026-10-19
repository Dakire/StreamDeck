using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using TouchDeck.Actions;
using TouchDeck.Platform.Windows;

namespace TouchDeck;

/// <summary>
/// Services partagés par le deck, l'éditeur et les paramètres : le document, la sauvegarde automatique,
/// le rechargement à chaud, l'apparence (thème, animations) et l'ouverture des fenêtres.
/// </summary>
public static class AppServices
{
    public static DeckDocument Document { get; private set; } = null!;
    /// <summary>Message à afficher au démarrage (configuration invalide, migration…), ou null.</summary>
    public static string? StartupMessage { get; private set; }

    static readonly DispatcherTimer SaveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    static readonly DispatcherTimer ReloadTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    static FileSystemWatcher? _watcher;
    static bool _dirty, _suppressSave;
    static (string Theme, string IconStyle, bool Motion)? _applied;

    /// <summary>Grille automatique du deck (colonnes, lignes) pour l'écran actuel : l'éditeur s'en sert pour les profils en format « Automatique ».</summary>
    public static (int Columns, int Rows) AutoGrid { get; set; } = (5, 4);

    public static EditorWindow? Editor { get; private set; }
    static SettingsWindow? _settings;

    /// <summary>Moteur d'actions du deck (plateforme Windows, ou simulée avec --simulate).</summary>
    public static ActionEngine Engine { get; private set; } = null!;
    /// <summary>Mode simulation : les actions sont consignées et affichées, jamais envoyées au système.</summary>
    public static bool Simulated { get; private set; }
    static HttpClient? _http;

    /// <param name="simulate">true : aucune touche envoyée, aucun programme lancé (essais sans risque).</param>
    public static void Init(bool simulate = false)
    {
        Simulated = simulate;
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };   // le moteur gère le délai de chaque étape
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TouchDeck/1.0");
        PlatformServices platform;
        if (simulate)
        {
            var sim = new SimulatedPlatform();
            sim.Performed += what => Application.Current?.Dispatcher.BeginInvoke(() => Notifier.Info("Simulation : " + what));
            platform = sim.Services;
        }
        else platform = WindowsPlatform.Create();
        Engine = new ActionEngine(new ActionContext { Platform = platform, Http = _http });
        Log.Info($"Démarrage (dossier {ConfigStore.Dir}{(simulate ? ", simulation" : "")})");

        DeckConfig cfg;
        try
        {
            var result = ConfigStore.LoadWithInfo();
            cfg = result.Config;
            if (result.Migrated) StartupMessage = "Configuration mise à jour vers les profils. Copie de l'ancien fichier : config.v1.backup.json";
            else if (result.Warnings.Count > 0) StartupMessage = result.Warnings[0];
        }
        catch (Exception ex)
        {
            // Le fichier reste lisible par l'utilisateur : on le met de côté avant que la sauvegarde automatique ne le remplace
            string note = "";
            try
            {
                var copy = Path.Combine(ConfigStore.Dir, $"config.invalide-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Copy(ConfigStore.FilePath, copy, true);
                note = $" Ton fichier est conservé sous {Path.GetFileName(copy)}.";
            }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException) { Log.Warn("Copie du fichier invalide impossible : " + copyEx.Message); }
            cfg = ConfigStore.DefaultConfig();
            StartupMessage = "Config invalide : " + ex.Message + note;
            Log.Error("Configuration invalide au démarrage", ex);
        }

        cfg.StartWithWindows = AutoStart.IsEnabled();   // le registre fait foi
        Document = new DeckDocument(cfg);
        ConfigStore.CreateBackup(cfg);
        ApplyAppearance(force: true);

        Document.Changed += OnChanged;
        SaveTimer.Tick += (_, _) => { SaveTimer.Stop(); Flush(); };
        ReloadTimer.Tick += (_, _) => { ReloadTimer.Stop(); if (!ConfigStore.IsOwnWrite()) ReloadFromDisk(); };
        StartWatcher();
    }

    public static void Shutdown()
    {
        SaveTimer.Stop();
        Flush();
        _watcher?.Dispose();
        Engine?.Dispose();       // interrompt les pauses, requêtes et minuteurs en cours
        _http?.Dispose();
        Log.Info("Arrêt");
    }

    // ---------- sauvegarde et rechargement ----------

    static void OnChanged(DocumentChange change)
    {
        ApplyAppearance(force: false);
        if (_suppressSave) return;
        _dirty = true;
        SaveTimer.Stop();
        SaveTimer.Start();
    }

    /// <summary>Écrit immédiatement les modifications en attente.</summary>
    public static void Flush()
    {
        if (!_dirty) return;
        try
        {
            ConfigStore.Save(Document.Config);
            _dirty = false;
        }
        catch (Exception ex)
        {
            Notifier.Error("Sauvegarde impossible : " + ex.Message);
            Log.Error("Sauvegarde impossible", ex);
            SaveTimer.Start();   // nouvel essai dans 300 ms
        }
    }

    static void StartWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(ConfigStore.Dir, Path.GetFileName(ConfigStore.FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler h = (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => { ReloadTimer.Stop(); ReloadTimer.Start(); });
            _watcher.Changed += h; _watcher.Created += h; _watcher.Renamed += (_, _) => h(null!, null!);
        }
        // Le deck fonctionne sans rechargement automatique (le menu « Recharger » reste disponible), mais on le consigne
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Log.Error("Surveillance de config.json impossible : rechargement à chaud désactivé", ex);
        }
    }

    /// <summary>Relit config.json (modification externe ou demande de l'utilisateur) ; une configuration invalide est refusée sans rien perdre.</summary>
    public static void ReloadFromDisk()
    {
        try
        {
            if (_dirty) Flush();
            var result = ConfigStore.LoadWithInfo();
            _suppressSave = true;
            try { Document.Reset(result.Config); }
            finally { _suppressSave = false; }
            Notifier.Info("Configuration rechargée");
        }
        catch (Exception ex)
        {
            Notifier.Error("Config invalide : " + ex.Message);
            Log.Error("Rechargement de la configuration refusé", ex);
        }
    }

    // ---------- apparence ----------

    static void ApplyAppearance(bool force)
    {
        var c = Document.Config;
        var now = (c.Theme, c.IconStyle, c.ReduceMotion);
        if (!force && _applied == now) return;
        bool iconsChanged = _applied == null || _applied.Value.IconStyle != c.IconStyle;
        _applied = now;
        IconCatalog.UseFluentForEmoji = c.IconStyle != "emoji";
        Motion.Apply(c.ReduceMotion);
        Theme.Apply(c.Theme);
        if (iconsChanged && !force) Theme.RaiseChanged();   // redessine les icônes même si le thème n'a pas bougé
    }

    // ---------- fenêtres ----------

    /// <summary>Ouvre l'éditeur (une seule instance), éventuellement sur un bouton précis.</summary>
    public static void OpenEditor(Place? place = null, int index = -1)
    {
        if (Editor == null)
        {
            Editor = new EditorWindow();
            Editor.Closed += (_, _) => Editor = null;
            if (Application.Current.MainWindow is { IsVisible: true } main) Editor.Owner = main;
            Editor.Show();
        }
        Editor.GoTo(place, index);
        if (Editor.WindowState == WindowState.Minimized) Editor.WindowState = WindowState.Normal;
        Editor.Activate();
    }

    public static void OpenSettings()
    {
        if (_settings == null)
        {
            _settings = new SettingsWindow();
            _settings.Closed += (_, _) => _settings = null;
            Window? owner = Editor ?? Application.Current.MainWindow;
            if (owner is { IsVisible: true }) _settings.Owner = owner;
            _settings.Show();
        }
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }
}
