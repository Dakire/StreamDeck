using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Integrations;

/// <summary>
/// Base des intégrations qui parlent HTTP à un appareil ou un logiciel local : un client, un relevé périodique de l'état,
/// des erreurs réseau traduites en messages lisibles, et la perte de connexion détectée quand le relevé échoue plusieurs fois.
/// </summary>
public abstract class PolledHttpIntegration : IntegrationBase, IDisposable
{
    /// <summary>Nombre de relevés consécutifs en échec avant de déclarer la connexion perdue (un relevé raté isolé est toléré).</summary>
    public const int FailuresBeforeLoss = 3;

    HttpClient? _http;
    CancellationTokenSource? _poll;

    /// <summary>Client HTTP de la connexion en cours.</summary>
    protected HttpClient Http => _http ?? throw new InvalidOperationException($"{Name} n'est pas connecté");
    /// <summary>Intervalle entre deux relevés d'état.</summary>
    protected abstract TimeSpan PollInterval { get; }

    /// <summary>Crée le client HTTP (les dérivés peuvent fournir un gestionnaire, par exemple pour un certificat auto-signé).</summary>
    protected virtual HttpClient CreateClient(IntegrationSettings settings) => new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>Lit l'état de l'appareil et publie les valeurs ; lève une exception si l'appareil ne répond pas.</summary>
    protected abstract Task PollAsync(IntegrationSettings settings, CancellationToken cancellation);

    /// <inheritdoc/>
    protected sealed override async Task ConnectCoreAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        _http = CreateClient(settings);
        try { await PollTranslatedAsync(settings, cancellation); }
        catch { _http.Dispose(); _http = null; throw; }
        _poll = new CancellationTokenSource();
        var token = _poll.Token;
        _ = Task.Run(() => PollLoopAsync(settings, token), CancellationToken.None);
    }

    async Task PollTranslatedAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        try { await PollAsync(settings, cancellation); }
        catch (HttpRequestException ex) { throw new InvalidOperationException(Unreachable(ex), ex); }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested) { throw new InvalidOperationException($"{Name} ne répond pas (délai dépassé)"); }
    }

    /// <summary>Message affiché quand l'appareil est injoignable ; les dérivés précisent où vérifier.</summary>
    protected virtual string Unreachable(HttpRequestException ex) => $"{Name} est injoignable : {ex.Message}";

    async Task PollLoopAsync(IntegrationSettings settings, CancellationToken stop)
    {
        int failures = 0;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(PollInterval, stop);
                try { await PollTranslatedAsync(settings, stop); failures = 0; }
                catch (InvalidOperationException ex) when (!stop.IsCancellationRequested)
                {
                    if (++failures >= FailuresBeforeLoss) { ConnectionLost(ex.Message); return; }
                }
            }
        }
        catch (OperationCanceledException) { /* arrêt demandé */ }
    }

    /// <inheritdoc/>
    protected override Task DisconnectCoreAsync()
    {
        _poll?.Cancel();
        _poll?.Dispose();
        _poll = null;
        _http?.Dispose();
        _http = null;
        return Task.CompletedTask;
    }

    /// <summary>Force un relevé tout de suite (après une commande, pour que le voyant suive sans attendre).</summary>
    protected async Task RefreshNowAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        try { await PollTranslatedAsync(settings, cancellation); }
        catch (InvalidOperationException) { /* le prochain relevé périodique réessaiera */ }
    }

    /// <summary>Libère le client et arrête le relevé.</summary>
    public void Dispose()
    {
        DisconnectCoreAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}