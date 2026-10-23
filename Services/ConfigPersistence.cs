using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace StreamDak;

/// <summary>
/// Enregistre le document 300 ms après la dernière modification et le recharge à chaud quand <c>config.json</c> change
/// ailleurs (éditeur de texte, synchronisation). Ses propres écritures sont reconnues par le hachage du contenu.
/// À utiliser depuis le thread d'interface.
/// </summary>
public sealed class ConfigPersistence : IDisposable
{
    readonly DeckDocument _doc;
    readonly ILogger<ConfigPersistence> _log;
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly DispatcherTimer _reloadTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    FileSystemWatcher? _watcher;
    bool _dirty, _suppressSave;

    /// <summary>S'abonne au document et démarre la surveillance de <c>config.json</c>.</summary>
    public ConfigPersistence(DeckDocument doc, ILogger<ConfigPersistence> log)
    {
        _doc = doc;
        _log = log;
        _doc.Changed += OnChanged;
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Flush(); };
        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); if (!ConfigStore.IsOwnWrite()) ReloadFromDisk(); };
        StartWatcher();
    }

    void OnChanged(DocumentChange change)
    {
        if (_suppressSave) return;
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Écrit immédiatement les modifications en attente.</summary>
    public void Flush()
    {
        if (!_dirty) return;
        try
        {
            ConfigStore.Save(_doc.Config);
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notifier.Error("Sauvegarde impossible : " + ex.Message);
            _log.LogError(ex, "Sauvegarde impossible");
            _saveTimer.Start();   // nouvel essai dans 300 ms
        }
    }

    void StartWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(ConfigStore.Dir, Path.GetFileName(ConfigStore.FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            void Changed(object? s, FileSystemEventArgs e) =>
                Application.Current?.Dispatcher.BeginInvoke(() => { _reloadTimer.Stop(); _reloadTimer.Start(); });
            _watcher.Changed += Changed;
            _watcher.Created += Changed;
            _watcher.Renamed += (s, e) => Changed(s, e);
        }
        // Le deck fonctionne sans rechargement automatique (le menu « Recharger » reste disponible), mais on le consigne
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _log.LogError(ex, "Surveillance de config.json impossible : rechargement à chaud désactivé");
        }
    }

    /// <summary>Relit config.json (modification externe ou demande de l'utilisateur) ; une configuration invalide est refusée sans rien perdre.</summary>
    public void ReloadFromDisk()
    {
        try
        {
            if (_dirty) Flush();
            var result = ConfigStore.LoadWithInfo();
            _suppressSave = true;
            try { _doc.Reset(result.Config); }
            finally { _suppressSave = false; }
            Notifier.Info("Configuration rechargée");
        }
        catch (Exception ex)
        {
            Notifier.Error("Config invalide : " + ex.Message);
            _log.LogError(ex, "Rechargement de la configuration refusé");
        }
    }

    /// <summary>Enregistre ce qui reste et arrête la surveillance (fermeture de l'application).</summary>
    public void Dispose()
    {
        _saveTimer.Stop();
        _reloadTimer.Stop();
        Flush();
        _watcher?.Dispose();
        _doc.Changed -= OnChanged;
    }
}
