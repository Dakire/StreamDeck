using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Integrations;

/// <summary>Base d'une intégration : état de connexion, valeurs d'état, notifications. Les classes dérivées implémentent la connexion et les commandes.</summary>
public abstract class IntegrationBase : IIntegration
{
    readonly object _gate = new();
    readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    ConnectionState _state = ConnectionState.Disconnected;

    /// <inheritdoc/>
    public abstract string Id { get; }
    /// <inheritdoc/>
    public abstract string Name { get; }
    /// <inheritdoc/>
    public abstract string Category { get; }
    /// <inheritdoc/>
    public virtual string Icon => "PlugConnected24";
    /// <inheritdoc/>
    public abstract string Help { get; }
    /// <inheritdoc/>
    public abstract IReadOnlyList<SettingField> Fields { get; }
    /// <inheritdoc/>
    public abstract IReadOnlyList<IntegrationCommand> Commands { get; }

    /// <inheritdoc/>
    public ConnectionState State { get { lock (_gate) return _state; } }
    /// <inheritdoc/>
    public string? Error { get; private set; }
    /// <inheritdoc/>
    public event Action? StateChanged;
    /// <inheritdoc/>
    public event Action<IReadOnlyCollection<string>>? ValuesChanged;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Values { get { lock (_gate) return new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase); } }

    /// <summary>Change l'état de la connexion et prévient les abonnés (sans effet si rien ne change). Un état hors « connecté » vide les valeurs.</summary>
    protected void SetState(ConnectionState state, string? error = null)
    {
        bool changed;
        string[] cleared = Array.Empty<string>();
        lock (_gate)
        {
            changed = _state != state || Error != error;
            _state = state;
            Error = error;
            if (state != ConnectionState.Connected && _values.Count > 0) { cleared = _values.Keys.ToArray(); _values.Clear(); }
        }
        if (cleared.Length > 0) ValuesChanged?.Invoke(cleared);
        if (changed) StateChanged?.Invoke();
    }

    /// <summary>Publie des valeurs d'état ; seules celles qui changent sont signalées.</summary>
    protected void SetValues(IEnumerable<KeyValuePair<string, string>> values)
    {
        var changed = new List<string>();
        lock (_gate)
        {
            foreach (var (key, value) in values)
                if (!_values.TryGetValue(key, out var old) || old != value) { _values[key] = value; changed.Add(key); }
        }
        if (changed.Count > 0) ValuesChanged?.Invoke(changed);
    }

    /// <summary>Publie une valeur d'état.</summary>
    protected void SetValue(string key, string value) => SetValues(new[] { KeyValuePair.Create(key, value) });

    /// <summary>Retire une valeur d'état.</summary>
    protected void RemoveValue(string key)
    {
        bool removed;
        lock (_gate) removed = _values.Remove(key);
        if (removed) ValuesChanged?.Invoke(new[] { key });
    }

    /// <summary>Retire toutes les valeurs qui commencent par ce préfixe.</summary>
    protected void RemoveValues(string prefix)
    {
        string[] removed;
        lock (_gate)
        {
            removed = _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var k in removed) _values.Remove(k);
        }
        if (removed.Length > 0) ValuesChanged?.Invoke(removed);
    }

    /// <inheritdoc/>
    public async Task ConnectAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        SetState(ConnectionState.Connecting);
        try
        {
            await ConnectCoreAsync(settings, cancellation);
            SetState(ConnectionState.Connected);
        }
        catch (OperationCanceledException)
        {
            SetState(ConnectionState.Disconnected);
            throw;
        }
        catch (Exception ex)
        {
            SetState(ConnectionState.Error, ex.Message);
            await SafeDisconnectAsync();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync()
    {
        await SafeDisconnectAsync();
        SetState(ConnectionState.Disconnected);
    }

    /// <inheritdoc/>
    public Task ReleaseAsync() => SafeDisconnectAsync();

    async Task SafeDisconnectAsync()
    {
        try { await DisconnectCoreAsync(); }
        catch (Exception ex) { Log.Warn($"{Name} : déconnexion en erreur : {ex.Message}"); }
    }

    /// <summary>Signale que la connexion établie vient de se perdre (le gestionnaire reconnectera avec une attente croissante).</summary>
    protected void ConnectionLost(string reason)
    {
        Log.Warn($"{Name} : connexion perdue ({reason})");
        SetState(ConnectionState.Error, reason);
    }

    /// <summary>Ouvre la connexion et lit l'état initial. Lève une exception avec un message affichable en cas d'échec.</summary>
    protected abstract Task ConnectCoreAsync(IntegrationSettings settings, CancellationToken cancellation);
    /// <summary>Ferme la connexion et libère les ressources.</summary>
    protected abstract Task DisconnectCoreAsync();
    /// <inheritdoc/>
    public abstract Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation);

    /// <summary>Texte d'erreur pour une commande qui exige un argument.</summary>
    protected static string RequireArgument(string? argument, string what) =>
        string.IsNullOrWhiteSpace(argument) ? throw new ArgumentException($"Indique {what}") : argument.Trim();
}