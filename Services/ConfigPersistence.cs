using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace StreamDak;

/// <summary>
/// Enregistre le document 300 ms après la dernière modification et le recharge à chaud quand <c>config.json</c> change
/// ailleurs (éditeur de texte, synchronisation). Ses propres écritures sont reconnues par le hachage du contenu.
/// À utiliser depuis le thread d'interface. Le thread d'interface ne fait que sérialiser (un instantané immuable) : l'écriture sur disque
/// est confiée à un écrivain unique du pool de threads, qui fusionne les instantanés successifs (seul le dernier compte).
/// </summary>
public sealed class ConfigPersistence : IDisposable
{
    readonly DeckDocument _doc;
    readonly ILogger<ConfigPersistence> _log;
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly DispatcherTimer _reloadTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly object _gate = new();     // état de l'écrivain
    readonly object _ioGate = new();   // une écriture à la fois, et jamais un instantané plus ancien après un plus récent
    FileSystemWatcher? _watcher;
    bool _dirty, _suppressSave, _failureNotified, _disposed;
    string? _pendingJson;
    long _pendingVersion, _version, _writtenVersion;
    bool _writing;

    /// <summary>S'abonne au document et démarre la surveillance de <c>config.json</c>.</summary>
    public ConfigPersistence(DeckDocument doc, ILogger<ConfigPersistence> log)
    {
        _doc = doc;
        _log = log;
        _doc.Changed += OnChanged;
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); QueueWrite(); };
        _reloadTimer.Tick += async (_, _) => { _reloadTimer.Stop(); await ReloadIfExternalAsync(); };
        StartWatcher();
    }

    void OnChanged(DocumentChange change)
    {
        if (_suppressSave) return;
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Écrit immédiatement, sur ce thread, les modifications en attente (fermeture, rechargement demandé, tests).</summary>
    public void Flush()
    {
        DrainWriter();
        if (!_dirty) return;
        string json;
        long version;
        lock (_gate) { json = ConfigStore.Serialize(_doc.Config); version = ++_version; _pendingJson = null; }
        try
        {
            WriteSnapshot(json, version);
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportFailure(ex);
            _saveTimer.Start();   // nouvel essai dans 300 ms
        }
    }

    /// <summary>Écrit sur place l'instantané en attente (pas encore repris par l'écrivain) et attend l'écriture en cours : rien n'est perdu à la fermeture.</summary>
    void DrainWriter()
    {
        string? json;
        long version;
        lock (_gate) { json = _pendingJson; version = _pendingVersion; _pendingJson = null; }
        if (json != null)
        {
            try { WriteSnapshot(json, version); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ReportFailure(ex); _dirty = true; return; }
        }
        lock (_ioGate) { /* attend la fin de l'écriture éventuellement en cours */ }
    }

    /// <summary>Prend l'instantané sur le thread d'interface puis confie l'écriture à l'écrivain du pool.</summary>
    void QueueWrite()
    {
        if (!_dirty || _disposed) return;
        string json = ConfigStore.Serialize(_doc.Config);
        _dirty = false;
        lock (_gate)
        {
            _pendingJson = json;
            _pendingVersion = ++_version;
            if (_writing) return;     // l'écrivain en cours reprendra ce dernier instantané
            _writing = true;
        }
        _ = Task.Run(WriteLoop);
    }

    void WriteLoop()
    {
        while (true)
        {
            string json;
            long version;
            lock (_gate)
            {
                if (_pendingJson == null) { _writing = false; return; }
                json = _pendingJson;
                version = _pendingVersion;
                _pendingJson = null;
            }
            try { WriteSnapshot(json, version); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (_gate) _writing = false;
                ReportFailure(ex);
                _ui.BeginInvoke(() => { _dirty = true; if (!_disposed) _saveTimer.Start(); });   // nouvel essai depuis le thread d'interface
                return;
            }
            catch (Exception ex)
            {
                lock (_gate) _writing = false;
                _log.LogError(ex, "Écriture de config.json interrompue");
                return;
            }
        }
    }

    void WriteSnapshot(string json, long version)
    {
        lock (_ioGate)
        {
            if (version < _writtenVersion) return;   // un instantané plus récent est déjà écrit
            ConfigStore.SaveJson(json);
            _writtenVersion = version;
            _failureNotified = false;
        }
    }

    /// <summary>Une seule notification par panne (sinon une toast toutes les 300 ms tant que le disque refuse).</summary>
    void ReportFailure(Exception ex)
    {
        _log.LogError(ex, "Sauvegarde impossible");
        bool first;
        lock (_gate) { first = !_failureNotified; _failureNotified = true; }
        if (first) _ui.BeginInvoke(() => Notifier.Error("Sauvegarde impossible : " + ex.Message));
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

    /// <summary>Relecture et hachage du fichier hors du thread d'interface : seule une vraie modification externe recharge.</summary>
    async Task ReloadIfExternalAsync()
    {
        try
        {
            if (_disposed) return;
            bool own = await Task.Run(ConfigStore.IsOwnWrite);
            if (!own && !_disposed) ReloadFromDisk();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Surveillance de config.json : lecture impossible");
        }
    }

    /// <summary>Relit config.json (modification externe ou demande de l'utilisateur) ; une configuration invalide est refusée sans rien perdre.</summary>
    public void ReloadFromDisk()
    {
        try
        {
            // Des modifications non enregistrées passent d'abord sur le disque : la relecture qui suit ne les perd pas, mais elle ne voit
            // alors pas la modification externe (elle est écrasée par la nôtre). On le dit au lieu de le taire.
            if (_dirty)
            {
                _log.LogWarning("Modification externe de config.json ignorée : des changements locaux non enregistrés ont été écrits à la place");
                Flush();
                Notifier.Info("Modification externe ignorée : des changements non enregistrés ont été conservés");
                return;
            }
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
        _disposed = true;
        _watcher?.Dispose();
        _doc.Changed -= OnChanged;
    }
}
