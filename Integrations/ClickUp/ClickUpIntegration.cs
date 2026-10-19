using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Integrations.ClickUp;

/// <summary>
/// ClickUp par son API v2 (https://developer.clickup.com/reference) : créer une tâche dans une liste, changer le statut d'une tâche,
/// démarrer et arrêter le minuteur. Jeton personnel (Paramètres ClickUp › Apps › Jeton d'API, « pk_… »), envoyé tel quel
/// dans l'en-tête Authorization.
/// </summary>
public sealed class ClickUpIntegration : RestIntegrationBase
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "clickup";
    string _list = "", _team = "";

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "ClickUp";
    /// <inheritdoc/>
    public override string Category => "ClickUp";
    /// <inheritdoc/>
    public override string Icon => "TaskListSquareLtr24";
    /// <inheritdoc/>
    public override string Help => "Crée un jeton personnel dans ClickUp (avatar › Paramètres › Apps › Jeton d'API). L'identifiant d'une liste se lit dans son adresse web ; celui de l'espace de travail (team) dans l'adresse de l'application.";
    /// <inheritdoc/>
    protected override string DefaultBaseUrl => "https://api.clickup.com/api/v2";

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("token", "Jeton d'API", SettingKind.Secret, null, "Commence par pk_."),
        new SettingField("list", "Liste par défaut", SettingKind.Text, null, "Identifiant de la liste où créer les tâches."),
        new SettingField("team", "Espace de travail", SettingKind.Text, null, "Identifiant de l'espace de travail, pour le minuteur."),
    };

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("task", "Nouvelle tâche", "Crée une tâche dans la liste par défaut.", "le titre de la tâche", true, null, "TaskListSquareLtr24", "#5B4FC9"),
        new IntegrationCommand("status", "Changer le statut", "Change le statut d'une tâche.", "« identifiant de la tâche=statut » (86abc=en cours)", true, null, "CheckmarkCircle24", "#5B4FC9"),
        new IntegrationCommand("timer-start", "Démarrer le minuteur", "Démarre le minuteur sur une tâche.", "l'identifiant de la tâche", true, null, "Timer24", "#5B4FC9"),
        new IntegrationCommand("timer-stop", "Arrêter le minuteur", "Arrête le minuteur en cours.", null, false, null, "TimerOff24", "#5B4FC9"),
    };

    /// <inheritdoc/>
    protected override void Configure(HttpClient http, string token) => http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", token);

    /// <inheritdoc/>
    protected override async Task VerifyAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        _list = settings.Get("list");
        _team = settings.Get("team");
        await SendAsync(HttpMethod.Get, "user", null, cancellation);
    }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        switch (command.ToLowerInvariant())
        {
            case "task":
            {
                var name = RequireArgument(argument, "le titre de la tâche");
                if (_list.Length == 0) throw new InvalidOperationException("Renseigne la liste par défaut dans les réglages ClickUp");
                var created = await SendAsync(HttpMethod.Post, $"list/{Uri.EscapeDataString(_list)}/task", new JsonObject { ["name"] = name }, cancellation);
                return $"Tâche créée : {created["name"] ?? name}";
            }
            case "status":
            {
                var (task, status) = SplitPair(argument, "identifiant de la tâche=statut");
                await SendAsync(HttpMethod.Put, $"task/{Uri.EscapeDataString(task)}", new JsonObject { ["status"] = status }, cancellation);
                return $"Statut « {status} »";
            }
            case "timer-start":
            {
                var task = RequireArgument(argument, "l'identifiant de la tâche");
                await SendAsync(HttpMethod.Post, $"team/{Team()}/time_entries/start", new JsonObject { ["tid"] = task }, cancellation);
                return "Minuteur démarré";
            }
            case "timer-stop":
                await SendAsync(HttpMethod.Post, $"team/{Team()}/time_entries/stop", null, cancellation);
                return "Minuteur arrêté";
            default: throw new ArgumentException($"Commande ClickUp inconnue : « {command} »");
        }
    }

    string Team() => _team.Length == 0 ? throw new InvalidOperationException("Renseigne l'espace de travail dans les réglages ClickUp") : Uri.EscapeDataString(_team);
}
