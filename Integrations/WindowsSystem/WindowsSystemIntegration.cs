using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Integrations.WindowsSystem;

/// <summary>Média en cours de lecture (session SMTC de Windows).</summary>
public sealed record MediaInfo(string Title, string Artist, string App, bool Playing);

/// <summary>Source des mesures et des commandes média ; remplacée par un faux dans les tests.</summary>
public interface ISystemProbe : IDisposable
{
    /// <summary>Charge du processeur en pourcentage (0 à 100) depuis le relevé précédent.</summary>
    double CpuPercent();
    /// <summary>Mémoire vive utilisée en pourcentage (0 à 100).</summary>
    double MemoryPercent();
    /// <summary>Média en cours, ou null si aucune application média n'est active.</summary>
    Task<MediaInfo?> GetMediaAsync();
    /// <summary>Envoie une commande à la session média : « playpause », « next » ou « previous ». Retourne false si aucune session.</summary>
    Task<bool> SendMediaAsync(string command);
}

/// <summary>
/// Windows lui-même : charge du processeur (<c>{sys.cpu}</c>), mémoire (<c>{sys.ram}</c>) et média en cours de lecture
/// (<c>{sys.media.title}</c>, <c>{sys.media.artist}</c>, <c>{sys.media.playing}</c>), quelle que soit l'application (Spotify, navigateur,
/// lecteur vidéo…) grâce aux contrôles de transport multimédia de Windows. Aucun compte ni réglage : l'intégration s'active d'un clic.
/// </summary>
public sealed class WindowsSystemIntegration : IntegrationBase, IDisposable
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "sys";
    /// <summary>Intervalle entre deux relevés.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    readonly Func<ISystemProbe> _createProbe;
    ISystemProbe? _probe;
    CancellationTokenSource? _loop;

    /// <summary>Intégration branchée sur Windows.</summary>
    public WindowsSystemIntegration() : this(() => new WindowsProbe()) { }

    /// <summary>Intégration avec une source de mesures donnée (tests).</summary>
    public WindowsSystemIntegration(Func<ISystemProbe> createProbe) { _createProbe = createProbe; }

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "Système Windows";
    /// <inheritdoc/>
    public override string Category => "Système Windows";
    /// <inheritdoc/>
    public override string Icon => "Desktop24";
    /// <inheritdoc/>
    public override string Help => "Aucun réglage. Affiche la charge du processeur et de la mémoire, et le média en cours (Spotify, navigateur…) dans les titres des touches : {sys.cpu}, {sys.ram}, {sys.media.title}.";
    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = Array.Empty<SettingField>();

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("playpause", "Lecture / pause", "Lecture ou pause du média en cours, quelle que soit l'application.", null, false, "sys.media.playing", "Play24", "#047857"),
        new IntegrationCommand("next", "Piste suivante", "Passe au média suivant de l'application en cours.", null, false, null, "NextFrame24", "#047857"),
        new IntegrationCommand("previous", "Piste précédente", "Revient au média précédent de l'application en cours.", null, false, null, "PreviousFrame24", "#047857"),
    };

    /// <inheritdoc/>
    protected override async Task ConnectCoreAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        _probe = _createProbe();
        await SampleAsync(_probe);   // le premier relevé du processeur vaut 0 : il amorce le compteur
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellation);
        await SampleAsync(_probe);
        _loop = new CancellationTokenSource();
        var token = _loop.Token;
        var probe = _probe;
        _ = Task.Run(() => LoopAsync(probe, token), CancellationToken.None);
    }

    async Task LoopAsync(ISystemProbe probe, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(PollInterval, stop);
                try { await SampleAsync(probe); }
                catch (Exception ex) when (ex is not OperationCanceledException) { Log.Warn($"Système Windows : relevé en erreur : {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { /* arrêt demandé */ }
    }

    async Task SampleAsync(ISystemProbe probe)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            KeyValuePair.Create("cpu", Round(probe.CpuPercent())),
            KeyValuePair.Create("ram", Round(probe.MemoryPercent())),
        };
        MediaInfo? media = null;
        try { media = await probe.GetMediaAsync(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.Warn($"Système Windows : média illisible : {ex.Message}"); }
        values.Add(KeyValuePair.Create("media.title", media?.Title ?? ""));
        values.Add(KeyValuePair.Create("media.artist", media?.Artist ?? ""));
        values.Add(KeyValuePair.Create("media.app", media?.App ?? ""));
        values.Add(KeyValuePair.Create("media.playing", media?.Playing == true ? "true" : "false"));
        SetValues(values);
    }

    static string Round(double percent) => Math.Clamp((int)Math.Round(percent), 0, 100).ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    protected override Task DisconnectCoreAsync()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
        _probe?.Dispose();
        _probe = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        var probe = _probe ?? throw new InvalidOperationException("Le système Windows n'est pas connecté");
        if (command is not ("playpause" or "next" or "previous")) throw new ArgumentException($"Commande système inconnue : « {command} »");
        if (!await probe.SendMediaAsync(command)) throw new InvalidOperationException("Aucune application ne lit de média pour l'instant");
        await SampleAsync(probe);
        return command switch { "playpause" => "Lecture / pause", "next" => "Piste suivante", _ => "Piste précédente" };
    }

    /// <summary>Arrête les relevés.</summary>
    public void Dispose()
    {
        DisconnectCoreAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
