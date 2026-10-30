using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Integrations;

namespace StreamDak.Audio;

/// <summary>Un canal d'un mélangeur : identifiant du fournisseur, libellé, icône Fluent et ce que son réglage change réellement.</summary>
/// <param name="Id">Identifiant du canal chez le fournisseur (« game », « chatCapture »).</param>
/// <param name="Label">Libellé affiché (« Jeu »).</param>
/// <param name="Icon">Nom d'icône Fluent.</param>
/// <param name="Controls">Ce que le réglage change, en toutes lettres (« Mélangeur Sonar (pas le volume Windows) »).</param>
public sealed record MixerChannel(string Id, string Label, string Icon, string Controls);

/// <summary>
/// Un mélangeur audio à canaux (Sonar aujourd'hui ; Wave Link ou Voicemeeter plus tard) : il liste ses canaux, relit leur niveau et les règle en pourcentage de 0 à 100.
/// La conversion vers l'échelle de l'API du fournisseur (0 à 1, décibels…) reste dans le fournisseur : le reste de l'application ne connaît que des pourcentages.
/// </summary>
public interface IAudioMixerProvider
{
    /// <summary>Identifiant du fournisseur (« sonar »).</summary>
    string Id { get; }
    /// <summary>Nom affiché (« Sonar »).</summary>
    string Name { get; }
    /// <summary>Canaux proposés, dans l'ordre d'affichage.</summary>
    IReadOnlyList<MixerChannel> Channels { get; }
    /// <summary>Un niveau ou la disponibilité a peut-être changé (peut être levé depuis n'importe quel thread).</summary>
    event Action? Changed;
    /// <summary>Description d'un canal, même inconnu (libellé et icône par défaut).</summary>
    MixerChannel Describe(string channel);
    /// <summary>Niveau du canal, ou null avec <paramref name="problem"/> qui dit pourquoi (fournisseur éteint, canal absent). Ne lève jamais d'exception.</summary>
    LevelReading? Read(string channel, out string problem);
    /// <summary>Règle le niveau (0 à 100). Lève une exception à message affichable en cas d'échec.</summary>
    Task SetLevelAsync(string channel, int percent, CancellationToken cancellation = default);
    /// <summary>Coupe ou rétablit. Lève une exception à message affichable en cas d'échec.</summary>
    Task SetMutedAsync(string channel, bool muted, CancellationToken cancellation = default);
}

/// <summary>Source de niveau d'un canal d'un <see cref="IAudioMixerProvider"/> : ce que lisent et règlent les curseurs du deck.</summary>
public class MixerLevelSource : ILevelSource, IDisposable
{
    readonly IAudioMixerProvider _provider;
    readonly bool _owns;
    readonly string _channel;
    Action? _changed;

    /// <summary>Source pour <paramref name="channel"/> ; <paramref name="ownsProvider"/> : la source libère le fournisseur avec elle.</summary>
    public MixerLevelSource(IAudioMixerProvider provider, string channel, bool ownsProvider = false)
    {
        _provider = provider;
        _channel = channel;
        _owns = ownsProvider;
        _provider.Changed += OnProviderChanged;
    }

    void OnProviderChanged() => _changed?.Invoke();

    /// <inheritdoc/>
    public string Title => _provider.Name + " · " + _provider.Describe(_channel).Label;
    /// <inheritdoc/>
    public string Controls => _provider.Describe(_channel).Controls;
    /// <inheritdoc/>
    public string Icon => _provider.Describe(_channel).Icon;
    /// <inheritdoc/>
    public TimeSpan? PollInterval => null;
    /// <inheritdoc/>
    public event Action? Changed { add => _changed += value; remove => _changed -= value; }

    /// <inheritdoc/>
    public LevelReading? Read(out string problem) => _provider.Read(_channel, out problem);

    /// <inheritdoc/>
    public Task SetLevelAsync(int percent) => _provider.SetLevelAsync(_channel, Math.Clamp(percent, 0, 100));

    /// <inheritdoc/>
    public Task SetMutedAsync(bool muted) => _provider.SetMutedAsync(_channel, muted);

    /// <summary>Cesse d'écouter le fournisseur (et le libère s'il appartient à cette source).</summary>
    public void Dispose()
    {
        _provider.Changed -= OnProviderChanged;
        if (_owns && _provider is IDisposable disposable) disposable.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Source de niveau : un canal du mélangeur Sonar (raccourci de <see cref="MixerLevelSource"/> sur <see cref="SonarMixerProvider"/>).</summary>
public sealed class SonarLevelSource : MixerLevelSource
{
    /// <summary>Source pour le canal <paramref name="channel"/> (nom Sonar : « game », « chatCapture »…).</summary>
    public SonarLevelSource(IntegrationManager integrations, string channel) : base(new SonarMixerProvider(integrations), channel, ownsProvider: true) { }
}

/// <summary>
/// Fournisseur Sonar : lit les valeurs du connecteur « sonar » (relevé léger du connecteur, sans appel supplémentaire) et le règle par ses commandes ;
/// la conversion vers les routes de l'API (0 à 1) est celle du connecteur.
/// </summary>
public sealed class SonarMixerProvider : IAudioMixerProvider, IDisposable
{
    /// <summary>Identifiant du connecteur Sonar.</summary>
    public const string ConnectorId = "sonar";
    readonly IntegrationManager _integrations;
    Action? _changed;

    /// <summary>Fournisseur branché sur le gestionnaire d'intégrations.</summary>
    public SonarMixerProvider(IntegrationManager integrations)
    {
        _integrations = integrations;
        _integrations.ValuesChanged += OnValues;
        _integrations.StateChanged += OnState;
    }

    /// <inheritdoc/>
    public string Id => ConnectorId;
    /// <inheritdoc/>
    public string Name => "Sonar";
    /// <inheritdoc/>
    public IReadOnlyList<MixerChannel> Channels { get; } = LevelOptions.SonarChannels.Select(c => Build(c.Channel, c.Label)).ToList();
    /// <inheritdoc/>
    public event Action? Changed { add => _changed += value; remove => _changed -= value; }

    static MixerChannel Build(string channel, string label) => new(channel, label, LevelOptions.ChannelIcon(channel),
        channel.Equals("chatCapture", StringComparison.OrdinalIgnoreCase) ? "Micro dans le mélangeur Sonar (pas le gain de la carte)" : "Mélangeur Sonar (pas le volume Windows)");

    /// <inheritdoc/>
    public MixerChannel Describe(string channel) =>
        Channels.FirstOrDefault(c => c.Id.Equals(channel, StringComparison.OrdinalIgnoreCase)) ?? Build(channel, LevelOptions.ChannelLabel(channel));

    void OnValues(IReadOnlyCollection<string> keys)
    {
        foreach (var key in keys)
            if (key.StartsWith(ConnectorId + ".", StringComparison.OrdinalIgnoreCase)) { _changed?.Invoke(); return; }
    }

    void OnState(IIntegration integration)
    {
        if (integration.Id == ConnectorId) _changed?.Invoke();
    }

    /// <inheritdoc/>
    public LevelReading? Read(string channel, out string problem)
    {
        problem = "";
        var sonar = _integrations.Find(ConnectorId);
        if (sonar == null) { problem = "Le connecteur Sonar est indisponible"; return null; }
        if (!_integrations.IsEnabled(ConnectorId)) { problem = "Active Sonar dans Paramètres › Intégrations"; return null; }
        if (sonar.State != ConnectionState.Connected)
        {
            problem = sonar.State == ConnectionState.Error && sonar.Error != null ? sonar.Error : "Connexion à Sonar…";
            return null;
        }
        if (!int.TryParse(_integrations.GetValue($"{ConnectorId}.{channel}.volume"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
        {
            problem = $"Sonar n'expose pas le canal « {channel} » (commande Diagnostic)";
            return null;
        }
        bool? muted = _integrations.GetValue($"{ConnectorId}.{channel}.muted") is { } m ? m == "true" : null;
        return new LevelReading(Math.Clamp(percent, 0, 100), muted);
    }

    IIntegration Connected() => _integrations.Find(ConnectorId) is { State: ConnectionState.Connected } sonar ? sonar : throw new InvalidOperationException("Sonar n'est pas connecté");

    /// <inheritdoc/>
    public Task SetLevelAsync(string channel, int percent, CancellationToken cancellation = default) =>
        Connected().ExecuteAsync("volume", $"{channel}={Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture)}", cancellation);

    /// <inheritdoc/>
    public Task SetMutedAsync(string channel, bool muted, CancellationToken cancellation = default) =>
        Connected().ExecuteAsync(muted ? "mute" : "unmute", channel, cancellation);

    /// <summary>Cesse d'écouter le gestionnaire.</summary>
    public void Dispose()
    {
        _integrations.ValuesChanged -= OnValues;
        _integrations.StateChanged -= OnState;
    }
}
