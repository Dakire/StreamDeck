using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Integrations.Twitch;

/// <summary>
/// Twitch par l'API Helix (https://dev.twitch.tv/docs/api/reference/) : créer un clip, poser un marqueur de stream, lancer une
/// publicité, changer le titre, écrire dans le chat. StreamDak n'embarque aucune application Twitch : l'utilisateur crée la sienne
/// (console développeur Twitch), en copie l'identifiant client, et génère un jeton utilisateur avec les droits voulus. Aucun
/// secret de l'application n'est nécessaire ni stocké ; le jeton, lui, est chiffré (DPAPI).
/// </summary>
public sealed class TwitchIntegration : RestIntegrationBase
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "twitch";
    static readonly int[] AdLengths = { 30, 60, 90, 120, 150, 180 };
    string _userId = "";

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "Twitch";
    /// <inheritdoc/>
    public override string Category => "Twitch";
    /// <inheritdoc/>
    public override string Icon => "VideoClip24";
    /// <inheritdoc/>
    public override string Help =>
        "1) Crée une application sur dev.twitch.tv/console et copie son identifiant client. 2) Génère un jeton utilisateur avec les droits : clips:edit (clip), channel:manage:broadcast (marqueur, titre), channel:edit:commercial (publicité), user:write:chat (chat). Les commandes dont le droit manque répondent par un refus explicite.";
    /// <inheritdoc/>
    protected override string DefaultBaseUrl => "https://api.twitch.tv/helix";

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("clientid", "Identifiant client", SettingKind.Text, null, "Celui de ton application Twitch (public, ce n'est pas un secret)."),
        new SettingField("token", "Jeton utilisateur", SettingKind.Secret, null, "Jeton d'accès du compte qui diffuse."),
    };

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("clip", "Créer un clip", "Crée un clip des derniers instants du direct.", null, false, null, "VideoClip24", "#6D28D9"),
        new IntegrationCommand("marker", "Marqueur de stream", "Pose un marqueur dans la rediffusion.", "une description (facultatif)", false, null, "Flag24", "#6D28D9"),
        new IntegrationCommand("ad", "Publicité", "Lance une publicité (30, 60, 90, 120, 150 ou 180 secondes).", "la durée en secondes (30 par défaut)", false, null, "Megaphone24", "#B45309"),
        new IntegrationCommand("title", "Changer le titre", "Change le titre du direct.", "le nouveau titre", true, null, "TextEditStyle24", "#6D28D9"),
        new IntegrationCommand("chat", "Message dans le chat", "Écrit un message dans le chat de la chaîne.", "le message", true, null, "Chat24", "#6D28D9"),
    };

    /// <inheritdoc/>
    protected override void Configure(HttpClient http, string token) => http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + token);

    /// <inheritdoc/>
    protected override async Task VerifyAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        var clientId = settings.Get("clientid");
        if (clientId.Length == 0) throw new InvalidOperationException("Renseigne l'identifiant client de ton application Twitch");
        Http.DefaultRequestHeaders.TryAddWithoutValidation("Client-Id", clientId);
        var me = await SendAsync(HttpMethod.Get, "users", null, cancellation);
        var user = me["data"]?[0] ?? throw new InvalidOperationException("Twitch n'a pas reconnu le jeton");
        _userId = user["id"]?.ToString() ?? "";
        var login = user["login"]?.ToString() ?? "";
        SetValue("channel", login);
    }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        var id = Uri.EscapeDataString(_userId);
        switch (command.ToLowerInvariant())
        {
            case "clip":
            {
                var clip = await SendAsync(HttpMethod.Post, $"clips?broadcaster_id={id}", null, cancellation);
                return $"Clip créé : {clip["data"]?[0]?["edit_url"] ?? "(lien indisponible)"}";
            }
            case "marker":
            {
                var body = new JsonObject { ["user_id"] = _userId };
                if (!string.IsNullOrWhiteSpace(argument)) body["description"] = argument.Trim()[..Math.Min(argument.Trim().Length, 140)];
                await SendAsync(HttpMethod.Post, "streams/markers", body, cancellation);
                return "Marqueur posé";
            }
            case "ad":
            {
                int seconds = 30;
                if (!string.IsNullOrWhiteSpace(argument) && !int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
                    throw new ArgumentException("La durée doit être un nombre de secondes (30, 60, 90, 120, 150 ou 180)");
                if (Array.IndexOf(AdLengths, seconds) < 0) throw new ArgumentException("La durée d'une publicité est 30, 60, 90, 120, 150 ou 180 secondes");
                var ad = await SendAsync(HttpMethod.Post, "channels/commercial", new JsonObject { ["broadcaster_id"] = _userId, ["length"] = seconds }, cancellation);
                return ad["data"]?[0]?["message"]?.ToString() is { Length: > 0 } message ? message : $"Publicité de {seconds} s";
            }
            case "title":
            {
                var title = RequireArgument(argument, "le nouveau titre");
                await SendAsync(HttpMethod.Patch, $"channels?broadcaster_id={id}", new JsonObject { ["title"] = title }, cancellation);
                return $"Titre : {title}";
            }
            case "chat":
            {
                var message = RequireArgument(argument, "le message");
                var reply = await SendAsync(HttpMethod.Post, "chat/messages", new JsonObject { ["broadcaster_id"] = _userId, ["sender_id"] = _userId, ["message"] = message }, cancellation);
                if (reply["data"]?[0]?["is_sent"]?.GetValue<bool>() == false)
                    throw new InvalidOperationException($"Twitch n'a pas envoyé le message : {reply["data"]?[0]?["drop_reason"]?["message"] ?? "refusé"}");
                return "Message envoyé";
            }
            default: throw new ArgumentException($"Commande Twitch inconnue : « {command} »");
        }
    }
}
