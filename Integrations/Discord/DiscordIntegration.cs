using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Integrations.Discord;

/// <summary>
/// Discord par son RPC local officiel (https://docs.discord.com/developers/topics/rpc) : micro, sourdine, volumes d'entrée et de sortie, quitter le salon vocal.
/// Tout passe par le canal local du client de bureau : <b>aucune dépendance au focus ni au clavier</b>, Discord peut être réduit ou en arrière-plan, et même
/// quand un jeu tourne en administrateur. L'état (micro coupé, sourdine, volumes, salon) suit Discord en direct par abonnement à ses événements.
/// <para>
/// Limites de Discord, à ne pas contourner : les droits <c>rpc</c> et <c>rpc.voice.*</c> ne sont accordés qu'au <b>propriétaire de l'application</b> et à ses
/// testeurs (50 au plus) tant que Discord ne l'a pas approuvée. StreamDak n'embarque donc aucune application : l'utilisateur crée la sienne (portail des
/// développeurs), copie son identifiant et son secret, puis autorise StreamDak une fois dans Discord. Le secret et le jeton sont chiffrés (DPAPI) ;
/// le jeton de rafraîchissement, renouvelé à chaque usage, est réenregistré (<see cref="ISecretRotating"/>).
/// </para>
/// </summary>
public sealed class DiscordIntegration : IntegrationBase, IPairableIntegration, ISecretRotating, IDisposable
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "discord";
    /// <summary>Champ secret du jeton de rafraîchissement (rempli par l'association).</summary>
    public const string TokenField = "token";
    /// <summary>Droits demandés : RPC, lecture et écriture des réglages vocaux.</summary>
    public static readonly string[] Scopes = { "rpc", "rpc.voice.read", "rpc.voice.write" };

    static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    readonly object _gate = new();
    readonly HashSet<string> _retired = new(StringComparer.Ordinal);   // jetons déjà échangés : un réglage ancien ne doit pas les réutiliser
    HttpClient? _http;
    DiscordIpc? _ipc;
    string _clientId = "", _clientSecret = "", _redirect = "";
    string? _refresh, _access;
    DateTime _accessExpires;

    /// <summary>Préfixe du canal nommé (les tests en donnent un autre).</summary>
    public string PipePrefix { get; set; } = "discord-ipc-";
    /// <summary>Adresse d'échange des jetons OAuth2 de Discord (les tests en donnent une locale).</summary>
    public Uri TokenEndpoint { get; set; } = new("https://discord.com/api/oauth2/token");
    /// <summary>Gestionnaire HTTP remplaçable (tests) ; null : celui de .NET.</summary>
    public HttpMessageHandler? HttpHandler { get; set; }
    /// <summary>Attente maximale de la fenêtre d'autorisation de Discord.</summary>
    public TimeSpan AuthorizeTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <inheritdoc/>
    public event Action<string, string>? SecretRotated;

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "Discord";
    /// <inheritdoc/>
    public override string Category => "Discord";
    /// <inheritdoc/>
    public override string Icon => "Headset24";
    /// <inheritdoc/>
    public override string Help =>
        "Contrôle Discord sans raccourci clavier, même réduit ou derrière un jeu : micro, sourdine, volumes, quitter le salon vocal ; les voyants suivent Discord en direct. " +
        "1) Sur discord.com/developers/applications, crée une application (« New Application ») ; dans OAuth2, copie le Client ID et le Client Secret, et ajoute la redirection http://localhost. " +
        "2) Colle-les ci-dessous, active l'intégration, puis clique « Autoriser dans Discord » et accepte dans la fenêtre de Discord. " +
        "Discord réserve ces droits au propriétaire de l'application (et à ses testeurs) : c'est ton application, pour ton compte. Le client de bureau Discord doit être lancé sur ce PC.";

    /// <inheritdoc/>
    public string PairLabel => "Autoriser dans Discord";
    /// <inheritdoc/>
    public string PairHelp => "Discord doit être lancé sur ce PC. Une fenêtre d'autorisation s'ouvre dans Discord : accepte-la (2 minutes).";
    /// <inheritdoc/>
    public string PairedSecretField => TokenField;

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("clientid", "Client ID de ton application", SettingKind.Text, null, "OAuth2 › Client ID, sur discord.com/developers/applications (ce n'est pas un secret)."),
        new SettingField("clientsecret", "Client Secret", SettingKind.Secret, null, "OAuth2 › Client Secret (« Reset Secret » pour l'afficher). Chiffré pour ton compte Windows."),
        new SettingField("redirect", "Redirection OAuth2", SettingKind.Text, "http://localhost", "Doit figurer dans OAuth2 › Redirects de l'application."),
        new SettingField(TokenField, "Jeton d'accès", SettingKind.Secret, null, "Rempli par « Autoriser dans Discord ». Vide-le pour retirer l'autorisation."),
    };

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("togglemute", "Micro : couper / rétablir", "Coupe ou rétablit ton micro dans Discord ; le voyant est plein quand il est coupé.", null, false, "mute", "MicOff24", "#4752C4"),
        new IntegrationCommand("mute", "Micro : couper", "Coupe ton micro dans Discord.", null, false, "mute", "MicOff24", "#4752C4"),
        new IntegrationCommand("unmute", "Micro : rétablir", "Rétablit ton micro dans Discord.", null, false, null, "Mic24", "#4752C4"),
        new IntegrationCommand("toggledeafen", "Sourdine : activer / désactiver", "Coupe ou rétablit le son et le micro (sourdine) ; le voyant est plein en sourdine.", null, false, "deaf", "HeadphonesSoundWave24", "#4752C4"),
        new IntegrationCommand("deafen", "Sourdine : activer", "Met Discord en sourdine (son et micro coupés).", null, false, "deaf", "Headphones24", "#4752C4"),
        new IntegrationCommand("undeafen", "Sourdine : désactiver", "Sort de la sourdine.", null, false, null, "Headphones24", "#4752C4"),
        new IntegrationCommand("inputvolume", "Volume du micro (Discord)", "Règle le volume d'entrée de Discord (0 à 100), ou le change de +5 / -5.", "un niveau (0 à 100) ou +5 / -5", true, null, "Mic24", "#4752C4"),
        new IntegrationCommand("outputvolume", "Volume de sortie (Discord)", "Règle le volume de sortie de Discord (0 à 200), ou le change de +10 / -10.", "un niveau (0 à 200) ou +10 / -10", true, null, "Speaker224", "#4752C4"),
        new IntegrationCommand("leave", "Quitter le salon vocal", "Quitte le salon vocal en cours.", null, false, "voice", "CallEnd24", "#BE123C"),
    };

    // ---------------------------------------------------------------- connexion

    /// <inheritdoc/>
    protected override async Task ConnectCoreAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        ReadSettings(settings);
        var ipc = await OpenAsync(cancellation);
        try
        {
            await AuthenticateAsync(ipc, cancellation);
            ipc.Event += OnEvent;
            ipc.Closed += reason => ConnectionLost(reason);
            Publish(await ipc.RequestAsync("GET_VOICE_SETTINGS", null, cancellation));
            await RefreshChannelAsync(ipc, cancellation);
            foreach (var evt in new[] { "VOICE_SETTINGS_UPDATE", "VOICE_CHANNEL_SELECT" })
                await ipc.RequestAsync("SUBSCRIBE", null, cancellation, evt);
            lock (_gate) _ipc = ipc;
        }
        catch
        {
            await ipc.DisposeAsync();
            throw;
        }
    }

    void ReadSettings(IntegrationSettings settings)
    {
        _clientId = settings.Get("clientid");
        _clientSecret = settings.Secret("clientsecret").Trim();
        _redirect = settings.Get("redirect", "http://localhost");
        if (_clientId.Length == 0) throw new InvalidOperationException("Renseigne le Client ID de ton application Discord (voir l'aide de l'intégration)");
        var stored = settings.Secret(TokenField).Trim();
        lock (_gate)
        {
            if (stored.Length == 0) { _refresh = null; _access = null; }                     // autorisation retirée
            else if (!_retired.Contains(stored) && stored != _refresh) { _refresh = stored; _access = null; }   // nouvelle autorisation
            // sinon : réglage lu avant un renouvellement, le jeton en mémoire est le bon
        }
    }

    async Task<DiscordIpc> OpenAsync(CancellationToken cancellation)
    {
        var ipc = await DiscordIpc.OpenAsync(PipePrefix, cancellation)
                  ?? throw new InvalidOperationException("Discord n'est pas lancé sur ce PC (canal local introuvable)");
        try
        {
            using var timeout = Linked(cancellation);
            await ipc.HandshakeAsync(_clientId, timeout.Token);
            return ipc;
        }
        catch (DiscordRpcException ex)
        {
            await ipc.DisposeAsync();
            throw new InvalidOperationException(ex.Code == 4000 ? "Discord refuse ce Client ID : vérifie-le" : "Discord refuse la connexion : " + ex.Message, ex);
        }
        catch
        {
            await ipc.DisposeAsync();
            throw;
        }
    }

    /// <summary>AUTHENTICATE avec le jeton d'accès en mémoire, sinon un jeton neuf obtenu par le jeton de rafraîchissement (qui est alors renouvelé).</summary>
    async Task AuthenticateAsync(DiscordIpc ipc, CancellationToken cancellation)
    {
        string? access;
        lock (_gate) access = _accessExpires > DateTime.UtcNow.AddMinutes(10) ? _access : null;
        if (access != null)
        {
            try { await ipc.RequestAsync("AUTHENTICATE", new JsonObject { ["access_token"] = access }, cancellation); return; }
            catch (DiscordRpcException) { lock (_gate) _access = null; }   // révoqué ou expiré plus tôt : on en demande un neuf
        }
        string refresh;
        lock (_gate) refresh = _refresh ?? throw new InvalidOperationException("Clique « Autoriser dans Discord » dans Paramètres › Intégrations › Discord");
        await ExchangeAsync(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh }, cancellation);
        lock (_gate) access = _access;
        try { await ipc.RequestAsync("AUTHENTICATE", new JsonObject { ["access_token"] = access }, cancellation); }
        catch (DiscordRpcException ex) { throw new InvalidOperationException("Discord refuse l'autorisation : clique de nouveau « Autoriser dans Discord » (" + ex.Message + ")", ex); }
    }

    /// <summary>Échange un code ou un jeton de rafraîchissement contre des jetons ; retient le jeton d'accès et publie le nouveau jeton de rafraîchissement.</summary>
    async Task<string> ExchangeAsync(Dictionary<string, string> grant, CancellationToken cancellation)
    {
        if (_clientSecret.Length == 0) throw new InvalidOperationException("Renseigne le Client Secret de ton application Discord");
        grant["client_id"] = _clientId;
        grant["client_secret"] = _clientSecret;
        if (_redirect.Length > 0) grant["redirect_uri"] = _redirect;
        var http = Http();
        using var content = new FormUrlEncodedContent(grant);
        using var timeout = Linked(cancellation, TimeSpan.FromSeconds(10));
        HttpResponseMessage response;
        try { response = await http.PostAsync(TokenEndpoint, content, timeout.Token); }
        catch (HttpRequestException ex) { throw new InvalidOperationException("Discord (discord.com) est injoignable : vérifie la connexion Internet", ex); }
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            JsonObject? reply = null;
            try { reply = JsonNode.Parse(body) as JsonObject; } catch (JsonException) { /* corps illisible : traité ci-dessous */ }
            if (!response.IsSuccessStatusCode || reply?["access_token"] is null)
            {
                var error = reply?["error_description"]?.ToString() ?? reply?["error"]?.ToString() ?? $"HTTP {(int)response.StatusCode}";
                if (grant["grant_type"] == "refresh_token") lock (_gate) { _refresh = null; _access = null; }
                throw new InvalidOperationException("Discord refuse l'autorisation (" + error + ") : vérifie le Client Secret et la redirection, puis clique « Autoriser dans Discord »");
            }
            var refresh = reply["refresh_token"]?.ToString() ?? "";
            int seconds = reply["expires_in"] is JsonValue v && v.TryGetValue(out int s) ? s : 3600;
            lock (_gate)
            {
                if (_refresh != null) _retired.Add(_refresh);
                _access = reply["access_token"]!.ToString();
                _accessExpires = DateTime.UtcNow.AddSeconds(seconds);
                if (refresh.Length > 0) _refresh = refresh;
            }
            if (refresh.Length > 0 && grant["grant_type"] == "refresh_token") SecretRotated?.Invoke(TokenField, refresh);
            return refresh;
        }
    }

    HttpClient Http()
    {
        lock (_gate) return _http ??= HttpHandler != null ? new HttpClient(HttpHandler, false) : new HttpClient();
    }

    static CancellationTokenSource Linked(CancellationToken cancellation, TimeSpan? after = null)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        cts.CancelAfter(after ?? RequestTimeout);
        return cts;
    }

    /// <inheritdoc/>
    protected override async Task DisconnectCoreAsync()
    {
        DiscordIpc? ipc;
        lock (_gate) { ipc = _ipc; _ipc = null; }
        if (ipc != null) await ipc.DisposeAsync();
    }

    // ---------------------------------------------------------------- association

    /// <inheritdoc/>
    public async Task<string> PairAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        _clientId = settings.Get("clientid");
        _clientSecret = settings.Secret("clientsecret").Trim();
        _redirect = settings.Get("redirect", "http://localhost");
        if (_clientId.Length == 0 || _clientSecret.Length == 0) throw new InvalidOperationException("Renseigne d'abord le Client ID et le Client Secret de ton application Discord");
        await using var ipc = await OpenAsync(cancellation);
        using var wait = Linked(cancellation, AuthorizeTimeout);
        JsonObject data;
        try
        {
            data = await ipc.RequestAsync("AUTHORIZE", new JsonObject { ["client_id"] = _clientId, ["scopes"] = new JsonArray(Scopes.Select(s => (JsonNode)s).ToArray()) }, wait.Token);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException("Pas de réponse dans Discord : relance l'autorisation et accepte-la dans la fenêtre de Discord"); }
        catch (DiscordRpcException ex) { throw new InvalidOperationException("Autorisation refusée par Discord : " + ex.Message, ex); }
        var code = data["code"]?.ToString();
        if (string.IsNullOrEmpty(code)) throw new InvalidOperationException("Discord n'a pas donné de code d'autorisation");
        lock (_gate) { if (_refresh != null) _retired.Add(_refresh); _refresh = null; }
        var refresh = await ExchangeAsync(new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = code }, cancellation);
        Log.Info("Discord : StreamDak autorisé");
        return refresh.Length > 0 ? refresh : throw new InvalidOperationException("Discord n'a pas donné de jeton de rafraîchissement");
    }

    // ---------------------------------------------------------------- état

    void OnEvent(string evt, JsonObject data)
    {
        if (evt == "VOICE_SETTINGS_UPDATE") Publish(data);
        else if (evt == "VOICE_CHANNEL_SELECT") _ = RefreshChannelSafeAsync();
    }

    async Task RefreshChannelSafeAsync()
    {
        DiscordIpc? ipc;
        lock (_gate) ipc = _ipc;
        if (ipc == null) return;
        using var timeout = Linked(CancellationToken.None);
        try { await RefreshChannelAsync(ipc, timeout.Token); }
        catch (Exception ex) when (ex is DiscordRpcException or OperationCanceledException) { Log.Warn("Discord : salon vocal illisible : " + ex.Message); }
    }

    async Task RefreshChannelAsync(DiscordIpc ipc, CancellationToken cancellation)
    {
        var channel = await ipc.RequestAsync("GET_SELECTED_VOICE_CHANNEL", null, cancellation);
        var name = channel["name"]?.ToString();
        SetValues(new[] { KeyValuePair.Create("voice", string.IsNullOrEmpty(name) ? "false" : "true"), KeyValuePair.Create("channel", name ?? "") });
    }

    /// <summary>Publie les réglages vocaux (réponse de GET/SET_VOICE_SETTINGS ou événement VOICE_SETTINGS_UPDATE).</summary>
    void Publish(JsonObject settings)
    {
        var values = new List<KeyValuePair<string, string>>();
        if (settings["mute"] is JsonValue m && m.TryGetValue(out bool mute)) values.Add(KeyValuePair.Create("mute", mute ? "true" : "false"));
        if (settings["deaf"] is JsonValue d && d.TryGetValue(out bool deaf)) values.Add(KeyValuePair.Create("deaf", deaf ? "true" : "false"));
        if (Volume(settings["input"]) is { } input) values.Add(KeyValuePair.Create("input.volume", input.ToString(CultureInfo.InvariantCulture)));
        if (Volume(settings["output"]) is { } output) values.Add(KeyValuePair.Create("output.volume", output.ToString(CultureInfo.InvariantCulture)));
        SetValues(values);
    }

    static int? Volume(JsonNode? device) =>
        device?["volume"] is JsonValue v && v.TryGetValue(out double volume) ? (int)Math.Round(volume) : null;

    bool Flag(string key) => Values.TryGetValue(key, out var v) && v == "true";

    // ---------------------------------------------------------------- commandes

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        DiscordIpc ipc;
        lock (_gate) ipc = _ipc ?? throw new InvalidOperationException("Discord n'est pas connecté");
        using var timeout = Linked(cancellation);
        switch (command.ToLowerInvariant())
        {
            case "togglemute": return await SetVoiceAsync(ipc, new JsonObject { ["mute"] = !Flag("mute") }, Flag("mute") ? "Micro rétabli" : "Micro coupé", timeout.Token);
            case "mute": return await SetVoiceAsync(ipc, new JsonObject { ["mute"] = true }, "Micro coupé", timeout.Token);
            case "unmute": return await SetVoiceAsync(ipc, new JsonObject { ["mute"] = false }, "Micro rétabli", timeout.Token);
            case "toggledeafen": return await SetVoiceAsync(ipc, new JsonObject { ["deaf"] = !Flag("deaf") }, Flag("deaf") ? "Sourdine désactivée" : "Sourdine activée", timeout.Token);
            case "deafen": return await SetVoiceAsync(ipc, new JsonObject { ["deaf"] = true }, "Sourdine activée", timeout.Token);
            case "undeafen": return await SetVoiceAsync(ipc, new JsonObject { ["deaf"] = false }, "Sourdine désactivée", timeout.Token);
            case "inputvolume":
            {
                int level = Level(argument, "input.volume", 100);
                return await SetVoiceAsync(ipc, new JsonObject { ["input"] = new JsonObject { ["volume"] = level } }, $"Micro Discord à {level} %", timeout.Token);
            }
            case "outputvolume":
            {
                int level = Level(argument, "output.volume", 200);
                return await SetVoiceAsync(ipc, new JsonObject { ["output"] = new JsonObject { ["volume"] = level } }, $"Sortie Discord à {level} %", timeout.Token);
            }
            case "leave":
                if (!Flag("voice")) return "Tu n'es dans aucun salon vocal";
                await Request(ipc, "SELECT_VOICE_CHANNEL", new JsonObject { ["channel_id"] = null, ["force"] = true }, timeout.Token);
                return "Salon vocal quitté";
            default: throw new ArgumentException($"Commande Discord inconnue : « {command} »");
        }
    }

    async Task<string> SetVoiceAsync(DiscordIpc ipc, JsonObject args, string done, CancellationToken cancellation)
    {
        Publish(await Request(ipc, "SET_VOICE_SETTINGS", args, cancellation));
        return done;
    }

    static async Task<JsonObject> Request(DiscordIpc ipc, string command, JsonObject args, CancellationToken cancellation)
    {
        try { return await ipc.RequestAsync(command, args, cancellation); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new TimeoutException("Discord ne répond pas"); }
        catch (DiscordRpcException ex) when (ex.Code == 4006) { throw new InvalidOperationException("Discord refuse : droit manquant, clique de nouveau « Autoriser dans Discord »", ex); }
        catch (DiscordRpcException ex) { throw new InvalidOperationException("Discord refuse : " + ex.Message, ex); }
    }

    /// <summary>Niveau absolu (« 60 ») ou relatif (« +5 », « -10 ») par rapport à la valeur publiée, borné de 0 à <paramref name="max"/>.</summary>
    int Level(string? argument, string key, int max)
    {
        var text = RequireArgument(argument, $"un niveau (0 à {max}) ou +5 / -5").Replace("%", "", StringComparison.Ordinal).Trim();
        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) throw new ArgumentException($"Niveau illisible : « {argument} » (0 à {max}, ou +5 / -5)");
        bool relative = text.StartsWith('+') || text.StartsWith('-');
        int current = Values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : 0;
        return Math.Clamp(relative ? current + n : n, 0, max);
    }

    /// <summary>Libère le client HTTP (fin de l'application).</summary>
    public void Dispose()
    {
        lock (_gate) { _http?.Dispose(); _http = null; }
    }
}
