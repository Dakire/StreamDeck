using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Integrations;

/// <summary>
/// Cycle de vie des intégrations : connecte celles qui sont activées dans la configuration, reconnecte avec une attente
/// croissante (2 s, 4 s… 60 s), applique les changements de réglages sans redémarrer, expose leurs états. Une intégration qui
/// échoue ne bloque ni l'interface ni les autres : chacune a sa propre boucle, son délai de connexion et ses journaux.
/// </summary>
public sealed class IntegrationManager : IDisposable
{
    /// <summary>Attente maximale d'une connexion.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    /// <summary>Attentes successives entre deux tentatives ; la dernière se répète.</summary>
    public static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(32), TimeSpan.FromSeconds(60),
    };

    sealed class Runner
    {
        public required CancellationTokenSource Cts { get; init; }
        public required string Fingerprint { get; init; }
        public Task Loop { get; set; } = Task.CompletedTask;
    }

    readonly DeckDocument _doc;
    readonly ISecretStore _secrets;
    readonly Func<TimeSpan, CancellationToken, Task> _delay;
    readonly Dictionary<string, IIntegration> _integrations;
    readonly Dictionary<string, Runner> _runners = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();
    bool _disposed;

    /// <summary>Crée le gestionnaire ; <paramref name="delay"/> remplace l'attente entre deux tentatives (tests).</summary>
    public IntegrationManager(IEnumerable<IIntegration> integrations, DeckDocument doc, ISecretStore secrets,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _doc = doc;
        _secrets = secrets;
        _delay = delay ?? Task.Delay;
        _integrations = integrations.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var integration in _integrations.Values)
        {
            integration.StateChanged += () => StateChanged?.Invoke(integration);
            integration.ValuesChanged += keys => ValuesChanged?.Invoke(keys.Select(k => integration.Id + "." + k).ToArray());
        }
    }

    /// <summary>Intégrations disponibles, dans l'ordre d'enregistrement.</summary>
    public IReadOnlyList<IIntegration> Integrations => _integrations.Values.ToList();

    /// <summary>L'état de connexion d'une intégration a changé (n'importe quel thread).</summary>
    public event Action<IIntegration>? StateChanged;

    /// <summary>Des valeurs d'état ont changé : clés qualifiées « id.clé » (n'importe quel thread).</summary>
    public event Action<IReadOnlyCollection<string>>? ValuesChanged;

    /// <summary>Le deck est visible (false) ou masqué (true) : les intégrations qui le savent ralentissent leurs relevés (<see cref="IBackgroundAware"/>).</summary>
    public void SetBackground(bool background)
    {
        foreach (var integration in _integrations.Values)
            if (integration is IBackgroundAware aware) aware.SetBackground(background);
    }

    /// <summary>Intégration d'identifiant donné, ou null.</summary>
    public IIntegration? Find(string? id) => id != null && _integrations.TryGetValue(id, out var i) ? i : null;

    /// <summary>true si l'intégration est activée dans la configuration.</summary>
    public bool IsEnabled(string id) => _doc.Config.Integrations?.TryGetValue(id, out var c) == true && c.Enabled;

    /// <summary>Commence à suivre la configuration : connecte les intégrations activées, puis applique chaque changement de réglages.</summary>
    public void Start()
    {
        MigratePlaintextSecrets();
        _doc.Changed += OnDocumentChanged;
        ApplyAll();
    }

    /// <summary>
    /// Un secret écrit en clair dans <c>config.json</c> (ancienne version, fichier édité à la main, profil importé) passe dans le magasin chiffré
    /// puis disparaît du fichier. Une valeur déjà présente dans le magasin n'est jamais écrasée. Retourne le nombre de secrets déplacés.
    /// </summary>
    public int MigratePlaintextSecrets()
    {
        var moved = new List<(string Integration, string Field)>();
        foreach (var integration in _integrations.Values)
        {
            var values = _doc.Config.Integrations?.GetValueOrDefault(integration.Id)?.Values;
            if (values == null) continue;
            foreach (var field in integration.Fields.Where(f => f.Kind == SettingKind.Secret))
            {
                var key = values.Keys.FirstOrDefault(k => string.Equals(k, field.Key, StringComparison.OrdinalIgnoreCase));
                if (key == null) continue;
                if (!string.IsNullOrEmpty(values[key]) && string.IsNullOrEmpty(_secrets.Get(SecretKey(integration.Id, field.Key))))
                    _secrets.Set(SecretKey(integration.Id, field.Key), values[key]);
                moved.Add((integration.Id, key));
            }
        }
        if (moved.Count == 0) return 0;
        _doc.UpdateSettings(c =>
        {
            foreach (var (id, key) in moved)
            {
                var values = c.Integrations?.GetValueOrDefault(id)?.Values;
                values?.Remove(key);
                if (values is { Count: 0 }) c.Integrations![id].Values = null;
            }
        }, "Secrets déplacés");
        Log.Info($"{moved.Count} secret(s) trouvé(s) en clair dans la configuration : déplacés dans le magasin chiffré");   // jamais leur valeur
        return moved.Count;
    }

    void OnDocumentChanged(DocumentChange change)
    {
        if (change.Kind != ChangeKind.Content) ApplyAll();
    }

    /// <summary>Relit la configuration : démarre, arrête ou relance les intégrations dont les réglages ont changé.</summary>
    public void ApplyAll()
    {
        foreach (var integration in _integrations.Values) Apply(integration);
    }

    void Apply(IIntegration integration)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var enabled = IsEnabled(integration.Id);
            var settings = SettingsFor(integration);
            var fingerprint = enabled ? Fingerprint(settings) : "";
            _runners.TryGetValue(integration.Id, out var runner);
            if (runner != null && runner.Fingerprint == fingerprint) return;
            var previous = runner != null ? Stop(integration, runner) : Task.CompletedTask;
            if (!enabled) { _runners.Remove(integration.Id); return; }

            var cts = new CancellationTokenSource();
            var created = new Runner { Cts = cts, Fingerprint = fingerprint };
            // La nouvelle boucle attend la fin de l'ancienne (déconnexion comprise) : sinon la fermeture tardive de l'ancienne
            // coupait la connexion neuve
            created.Loop = Task.Run(async () => { await previous; await RunAsync(integration, settings, cts.Token); });
            _runners[integration.Id] = created;
        }
    }

    static Task Stop(IIntegration integration, Runner runner)
    {
        runner.Cts.Cancel();
        return Task.Run(async () =>
        {
            try { await runner.Loop; } catch (OperationCanceledException) { /* arrêt demandé */ }
            await integration.DisconnectAsync();
            runner.Cts.Dispose();
        });
    }

    async Task RunAsync(IIntegration integration, IntegrationSettings settings, CancellationToken stop)
    {
        int attempt = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                timeout.CancelAfter(ConnectTimeout);
                await integration.ConnectAsync(settings, timeout.Token);
                Log.Info($"{integration.Name} : connecté");
                attempt = 0;
                await WaitForLossAsync(integration, stop);
                if (stop.IsCancellationRequested) break;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (OperationCanceledException)
            {
                Log.Warn($"{integration.Name} : connexion trop longue (plus de {ConnectTimeout.TotalSeconds:0} s)");
            }
            catch (Exception ex)
            {
                // Logiciel fermé, mauvais mot de passe… : attendu, pas une erreur de StreamDak ; consigné sans détail de pile
                Log.Warn($"{integration.Name} : connexion impossible : {ex.Message}");
            }

            try { await integration.ReleaseAsync(); } catch (Exception ex) { Log.Warn($"{integration.Name} : {ex.Message}"); }
            var wait = Backoff[Math.Min(attempt++, Backoff.Length - 1)];
            try { await _delay(wait, stop); }
            catch (OperationCanceledException) { break; }
        }
    }

    static async Task WaitForLossAsync(IIntegration integration, CancellationToken stop)
    {
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check() { if (integration.State != ConnectionState.Connected) lost.TrySetResult(); }
        integration.StateChanged += Check;
        try
        {
            Check();
            await lost.Task.WaitAsync(stop);
        }
        finally { integration.StateChanged -= Check; }
    }

    /// <summary>Réglages d'une intégration : valeurs de la configuration, secrets du magasin chiffré.</summary>
    public IntegrationSettings SettingsFor(IIntegration integration)
    {
        var cfg = _doc.Config.Integrations?.GetValueOrDefault(integration.Id);
        var values = new Dictionary<string, string>(cfg?.Values ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var secrets = integration.Fields.Where(f => f.Kind == SettingKind.Secret)
            .ToDictionary(f => f.Key, f => _secrets.Get(SecretKey(integration.Id, f.Key)) ?? "", StringComparer.OrdinalIgnoreCase);
        return new IntegrationSettings { Values = values, Secrets = secrets };
    }

    /// <summary>Clé du magasin de secrets pour un champ secret d'une intégration.</summary>
    public static string SecretKey(string integrationId, string field) => $"integration.{integrationId}.{field}";

    static string Fingerprint(IntegrationSettings s) =>
        string.Join("|", s.Values.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "=" + p.Value)
            .Concat(s.Secrets.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "#" + (p.Value.Length == 0 ? 0 : p.Value.GetHashCode()))));

    /// <summary>
    /// Essaie de se connecter avec ces réglages et dit si cela a marché. Si l'intégration est activée et connectée, le résultat
    /// est immédiat ; sinon elle se connecte puis se déconnecte (ou reste connectée si elle est activée).
    /// </summary>
    public async Task<string> TestAsync(string id, CancellationToken cancellation = default)
    {
        var integration = Find(id) ?? throw new ArgumentException($"Intégration inconnue : {id}");
        if (IsEnabled(id) && integration.State == ConnectionState.Connected) return $"{integration.Name} est connecté.";
        var settings = SettingsFor(integration);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            await integration.ConnectAsync(settings, timeout.Token);
            if (!IsEnabled(id)) await integration.DisconnectAsync();
            return $"Connexion à {integration.Name} réussie.";
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TimeoutException($"{integration.Name} ne répond pas (plus de {ConnectTimeout.TotalSeconds:0} s)");
        }
    }

    /// <summary>Valeur d'état « id.clé » (par exemple « obs.scene »), ou null si elle est inconnue ou si l'intégration n'est pas connectée.</summary>
    public string? GetValue(string qualifiedKey)
    {
        var dot = qualifiedKey.IndexOf('.');
        if (dot <= 0 || Find(qualifiedKey[..dot]) is not { State: ConnectionState.Connected } integration) return null;
        return integration.Values.TryGetValue(qualifiedKey[(dot + 1)..], out var v) ? v : null;
    }

    /// <summary>
    /// État « actif » (voyant ON / OFF) d'une étape d'intégration : vrai, faux, ou null si la commande n'a pas d'état ou si
    /// l'intégration n'est pas connectée.
    /// </summary>
    public bool? StateOf(DeckAction action)
    {
        if (Find(action.Type) is not { State: ConnectionState.Connected } integration) return null;
        var command = integration.Commands.FirstOrDefault(c => c.Name.Equals(action.Value, StringComparison.OrdinalIgnoreCase));
        if (command?.StateKey is not { } key) return null;
        key = key.Replace("{arg}", action.Args?.Trim() ?? "", StringComparison.Ordinal);
        if (!integration.Values.TryGetValue(key, out var value)) return null;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Arrête toutes les intégrations (fermeture de l'application).</summary>
    public void Dispose()
    {
        List<(IIntegration, Runner)> running;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            running = _runners.Select(p => (_integrations[p.Key], p.Value)).ToList();
            _runners.Clear();
        }
        _doc.Changed -= OnDocumentChanged;
        foreach (var (_, runner) in running) runner.Cts.Cancel();
        foreach (var (integration, runner) in running)
        {
            try { runner.Loop.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { /* annulé */ }
            try { integration.DisconnectAsync().Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { /* déjà fermée */ }
        }
    }
}