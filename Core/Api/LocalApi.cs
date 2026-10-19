using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TouchDeck.Actions;
using TouchDeck.Integrations;

namespace TouchDeck;

/// <summary>Ce que l'API locale demande à l'interface : naviguer et ouvrir des fenêtres (le thread d'interface reste du côté de l'application).</summary>
public interface IApiShell
{
    /// <summary>Navigation du deck (page, profil).</summary>
    INavigator Navigator { get; }
    /// <summary>Affiche le deck s'il est réduit.</summary>
    void ShowDeck();
    /// <summary>Ouvre l'éditeur.</summary>
    void OpenEditor();
    /// <summary>Ouvre les paramètres.</summary>
    void OpenSettings();
}

/// <summary>
/// API locale de TouchDeck : une requête JSON par ligne, une réponse JSON par ligne
/// (<c>{"cmd":"ping"}</c> → <c>{"ok":true,...}</c>). Commandes : ping, status, values, profile, page, run, show, edit, settings.
/// Le transport (canal nommé, réservé à l'utilisateur courant) est dans <see cref="LocalApiServer"/>.
/// </summary>
public sealed class LocalApi
{
    /// <summary>Version du protocole de l'API locale.</summary>
    public const int ProtocolVersion = 1;
    readonly DeckDocument _doc;
    readonly ActionEngine _engine;
    readonly IntegrationManager _integrations;
    readonly IApiShell _shell;

    /// <summary>API sur le document, le moteur d'actions, les intégrations et l'interface donnés.</summary>
    public LocalApi(DeckDocument doc, ActionEngine engine, IntegrationManager integrations, IApiShell shell)
    {
        _doc = doc;
        _engine = engine;
        _integrations = integrations;
        _shell = shell;
    }

    static JsonObject Ok(JsonObject? data = null)
    {
        var reply = data ?? new JsonObject();
        reply["ok"] = true;
        return reply;
    }

    static JsonObject Fail(string error) => new() { ["ok"] = false, ["error"] = error };

    /// <summary>Traite une requête (une ligne de JSON) et retourne la réponse. Ne lève jamais d'exception.</summary>
    public async Task<string> HandleAsync(string line, CancellationToken cancellation = default)
    {
        JsonObject reply;
        try
        {
            if (JsonNode.Parse(line) is not JsonObject request) reply = Fail("La requête doit être un objet JSON");
            else reply = await DispatchAsync(request, cancellation);
        }
        catch (JsonException) { reply = Fail("JSON invalide"); }
        catch (OperationCanceledException) { reply = Fail("Interrompu"); }
        catch (Exception ex)
        {
            Log.Warn($"API locale : {ex.Message}");
            reply = Fail(ex.Message);
        }
        return reply.ToJsonString();
    }

    async Task<JsonObject> DispatchAsync(JsonObject request, CancellationToken cancellation)
    {
        var cmd = request["cmd"]?.GetValue<string>()?.ToLowerInvariant();
        string Arg(string name) => request[name]?.ToString() ?? "";
        switch (cmd)
        {
            case "ping": return Ok(new JsonObject { ["protocol"] = ProtocolVersion });
            case "status": return Ok(Status());
            case "values": return Ok(Values());
            case "profile":
                if (_doc.FindProfile(Arg("name")) is not { } profile || !profile.Name.Equals(Arg("name"), StringComparison.OrdinalIgnoreCase))
                    return Fail($"Profil introuvable : {Arg("name")}");
                _shell.Navigator.SwitchProfile(profile.Name);
                return Ok();
            case "page":
                if (Arg("name").Length == 0) return Fail("Indique le nom de la page");
                _shell.Navigator.GoToPage(Arg("name"));
                return Ok();
            case "run": return await RunAsync(request, cancellation);
            case "show": _shell.ShowDeck(); return Ok();
            case "edit": _shell.OpenEditor(); return Ok();
            case "settings": _shell.OpenSettings(); return Ok();
            case "too-large": return Fail("Requête trop grande");
            case null: return Fail("Indique la commande (« cmd »)");
            default: return Fail($"Commande inconnue : « {cmd} »");
        }
    }

    JsonObject Status()
    {
        var profiles = new JsonArray();
        foreach (var p in _doc.Config.Profiles)
            profiles.Add(new JsonObject { ["name"] = p.Name, ["pages"] = new JsonArray(p.Pages.Select(pg => (JsonNode?)pg.Name).ToArray()) });
        var integrations = new JsonObject();
        foreach (var i in _integrations.Integrations)
            integrations[i.Id] = _integrations.IsEnabled(i.Id) ? i.State.ToString().ToLowerInvariant() : "disabled";
        return new JsonObject { ["protocol"] = ProtocolVersion, ["activeProfile"] = _doc.ActiveProfile.Name, ["profiles"] = profiles, ["integrations"] = integrations };
    }

    JsonObject Values()
    {
        var values = new JsonObject();
        foreach (var i in _integrations.Integrations)
            foreach (var (key, value) in i.Values) values[$"{i.Id}.{key}"] = value;
        return new JsonObject { ["values"] = values };
    }

    async Task<JsonObject> RunAsync(JsonObject request, CancellationToken cancellation)
    {
        var steps = new List<DeckAction>();
        if (request["actions"] is JsonArray array) steps.AddRange(array.Select(n => n.Deserialize<DeckAction>(ConfigStore.Options)!));
        else if (request["action"] is JsonObject single) steps.Add(single.Deserialize<DeckAction>(ConfigStore.Options)!);
        else return Fail("Indique « action » (un objet) ou « actions » (une liste) : {\"type\":\"hotkey\",\"value\":\"ctrl+c\"}");
        if (steps.Count == 0 || steps.Count > 20) return Fail("Entre 1 et 20 actions");

        var button = new DeckButton { Label = "API locale", Actions = steps };
        var issues = ActionValidator.Validate(button, _doc.Config, _doc.ActiveProfile).Where(i => i.Severity == Severity.Error).ToList();
        if (issues.Count > 0) return Fail(issues[0].Message);

        var result = await _engine.RunAsync(button, _shell.Navigator, cancellation);
        var reply = new JsonObject { ["outcome"] = result.Outcome.ToString().ToLowerInvariant() };
        if (result.Message != null) reply["message"] = result.Message;
        if (!result.Ok) { reply["ok"] = false; reply["error"] = result.Message ?? result.Outcome.ToString(); return reply; }
        return Ok(reply);
    }
}
