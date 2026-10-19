using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

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

    public static void Init()
    {
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
            catch { /* pas de fichier à conserver */ }
            cfg = ConfigStore.DefaultConfig();
            StartupMessage = "Config invalide : " + ex.Message + note;
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
        catch { /* pas de rechargement automatique */ }
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
        catch (Exception ex) { Notifier.Error("Config invalide : " + ex.Message); }
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
