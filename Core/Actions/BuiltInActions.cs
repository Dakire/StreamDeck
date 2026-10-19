using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Actions;

/// <summary>Les types d'action fournis avec TouchDeck. Les clés (Type, Value, Args) sont celles de config.json.</summary>
public static class BuiltInActions
{
    sealed class Handler : IActionHandler
    {
        readonly Func<DeckAction, StepContext, CancellationToken, Task<string?>> _run;
        public Handler(string type, TimeSpan? timeout, Func<DeckAction, StepContext, CancellationToken, Task<string?>> run)
        {
            Type = type; Timeout = timeout; _run = run;
        }
        public string Type { get; }
        public TimeSpan? Timeout { get; }
        public Task<string?> ExecuteAsync(DeckAction a, StepContext c, CancellationToken ct) => _run(a, c, ct);
    }

    static readonly TimeSpan Quick = TimeSpan.FromSeconds(5);
    static readonly TimeSpan Launch = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    static readonly Task<string?> Done = Task.FromResult<string?>(null);

    static Handler Sync(string type, Action<DeckAction, StepContext> run) =>
        new(type, Quick, (a, c, _) => { run(a, c); return Done; });

    /// <summary>Le lancement d'un programme peut bloquer (disque, antivirus) : hors du thread d'interface, avec délai maximal.</summary>
    static Handler Background(string type, TimeSpan timeout, Action<DeckAction, StepContext> run) =>
        new(type, timeout, async (a, c, ct) => { await Task.Run(() => run(a, c), ct).WaitAsync(ct); return null; });

    public static readonly IReadOnlyList<IActionHandler> All = new IActionHandler[]
    {
        Sync("hotkey", (a, c) => c.Services.Platform.Keyboard.SendHotkey(a.Value)),
        Sync("media", (a, c) =>
        {
            if (!ActionValidator.MediaKeys.Contains(a.Value.ToLowerInvariant())) throw new ArgumentException($"Touche média inconnue : « {a.Value} »");
            c.Services.Platform.Keyboard.SendHotkey(a.Value);
        }),
        Sync("text", (a, c) => c.Services.Platform.Keyboard.TypeText(a.Value)),
        Sync("page", (a, c) => c.Navigator.GoToPage(a.Value)),
        Sync("profile", (a, c) => c.Navigator.SwitchProfile(a.Value)),
        new Handler("delay", null, async (a, c, ct) =>
        {
            await c.Services.Delay(TimeSpan.FromMilliseconds(int.TryParse(a.Value, out var ms) && ms >= 0 ? ms : 100), ct);
            return null;
        }),
        Background("url", Launch, (a, c) => c.Services.Platform.Launcher.Open(CheckUrl(a.Value), null)),
        Background("launch", Launch, (a, c) => c.Services.Platform.Launcher.Open(a.Value, a.Args)),
        Background("script", Launch, (a, c) => c.Services.Platform.Launcher.RunScript(a.Value, a.Args)),
        new Handler("focus", TimeSpan.FromSeconds(10), Focus),
        new Handler("http", HttpTimeout, Http),
        new Handler("audio", Quick, (a, c, _) => Task.FromResult(Audio(a, c))),
        new Handler("timer", null, (a, c, _) => Task.FromResult<string?>(Timer(a, c))),
    };

    /// <summary>Ces protocoles n'ont rien à faire dans un bouton : ils servent à exécuter du code ou lire des fichiers par détour.</summary>
    static string CheckUrl(string value)
    {
        var v = value.Trim();
        if (Uri.TryCreate(v, UriKind.Absolute, out var uri) && ActionValidator.BlockedSchemes.Contains(uri.Scheme.ToLowerInvariant()))
            throw new InvalidOperationException($"Protocole « {uri.Scheme}: » non autorisé");
        return v;
    }

    static async Task<string?> Focus(DeckAction a, StepContext c, CancellationToken ct)
    {
        var windows = c.Services.Platform.Windows;
        if (windows.Activate(a.Value)) return null;
        // Fenêtre absente : on lance le programme indiqué (s'il y en a un) et on laisse la main aux actions suivantes
        if (string.IsNullOrWhiteSpace(a.Args)) throw new InvalidOperationException($"Fenêtre « {a.Value} » introuvable");
        c.Services.Platform.Launcher.Open(a.Args, null);
        await c.Services.Delay(TimeSpan.FromMilliseconds(1500), ct);
        windows.Activate(a.Value);
        return null;
    }

    /// <summary>Requête web (webhook) : GET sans corps, POST si un corps est indiqué (JSON s'il commence par { ou [).</summary>
    static async Task<string?> Http(DeckAction a, StepContext c, CancellationToken ct)
    {
        if (!Uri.TryCreate(a.Value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("L'adresse doit commencer par http:// ou https://");
        using var request = new HttpRequestMessage(string.IsNullOrEmpty(a.Args) ? HttpMethod.Get : HttpMethod.Post, uri);
        if (!string.IsNullOrEmpty(a.Args))
        {
            var body = a.Args.TrimStart();
            var type = body.StartsWith('{') || body.StartsWith('[') ? "application/json" : "text/plain";
            request.Content = new StringContent(a.Args, Encoding.UTF8, type);
        }
        using var response = await c.Services.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Le serveur a répondu {(int)response.StatusCode} {response.ReasonPhrase}");
        return null;
    }

    static string? Audio(DeckAction a, StepContext c)
    {
        var audio = c.Services.Platform.Audio;
        switch (a.Value.ToLowerInvariant())
        {
            case "mic-toggle":
                bool mic = !audio.IsMuted(AudioDevice.Microphone);
                audio.SetMute(AudioDevice.Microphone, mic);
                return mic ? "Micro coupé" : "Micro actif";
            case "mic-mute": audio.SetMute(AudioDevice.Microphone, true); return "Micro coupé";
            case "mic-unmute": audio.SetMute(AudioDevice.Microphone, false); return "Micro actif";
            case "out-toggle":
                bool muted = !audio.IsMuted(AudioDevice.Output);
                audio.SetMute(AudioDevice.Output, muted);
                return muted ? "Son coupé" : "Son rétabli";
            case "volume":
                if (!int.TryParse(a.Args, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) || level is < 0 or > 100)
                    throw new ArgumentException("Le volume doit être un nombre entre 0 et 100");
                audio.SetVolume(AudioDevice.Output, level);
                return $"Volume {level} %";
            default:
                throw new ArgumentException($"Commande audio inconnue : « {a.Value} »");
        }
    }

    /// <summary>Minuteur : démarre le compte à rebours du bouton, ou l'arrête s'il tourne déjà.</summary>
    static string? Timer(DeckAction a, StepContext c)
    {
        if (!int.TryParse(a.Value, out var seconds) || seconds <= 0)
            throw new ArgumentException("La durée du minuteur doit être un nombre de secondes");
        var countdowns = c.Services.Countdowns;
        return countdowns.Toggle(c.Button, TimeSpan.FromSeconds(seconds))
            ? $"Minuteur lancé : {Countdowns.Format(TimeSpan.FromSeconds(seconds))}"
            : "Minuteur arrêté";
    }
}
