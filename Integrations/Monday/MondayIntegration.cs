using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Integrations.Monday;

/// <summary>
/// monday.com par son API GraphQL (https://developer.monday.com/api-reference) : créer un élément dans un tableau et changer une
/// colonne de statut. Jeton personnel (avatar › Développeurs › Mon jeton d'accès). Les valeurs passent par des variables GraphQL,
/// jamais collées dans la requête.
/// </summary>
public sealed class MondayIntegration : RestIntegrationBase
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "monday";
    /// <summary>Version de l'API demandée (en-tête API-Version).</summary>
    public const string ApiVersion = "2025-04";
    string _board = "", _statusColumn = "status";

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "monday.com";
    /// <inheritdoc/>
    public override string Category => "monday.com";
    /// <inheritdoc/>
    public override string Icon => "BoardSplit24";
    /// <inheritdoc/>
    public override string Help => "Dans monday : avatar › Développeurs › Mon jeton d'accès. L'identifiant du tableau est le nombre de son adresse web ; celui de la colonne de statut se lit dans le menu de la colonne (« Identifiant de colonne »).";
    /// <inheritdoc/>
    protected override string DefaultBaseUrl => "https://api.monday.com";

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("token", "Jeton d'accès", SettingKind.Secret),
        new SettingField("board", "Tableau par défaut", SettingKind.Text, null, "Identifiant du tableau où créer les éléments."),
        new SettingField("statuscol", "Colonne de statut", SettingKind.Text, "status", "Identifiant de la colonne de statut."),
    };

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("item", "Nouvel élément", "Crée un élément dans le tableau par défaut.", "le nom de l'élément", true, null, "BoardSplit24", "#BE123C"),
        new IntegrationCommand("status", "Changer le statut", "Change le statut d'un élément.", "« identifiant de l'élément=libellé » (123456=En cours)", true, null, "CheckmarkCircle24", "#BE123C"),
    };

    /// <inheritdoc/>
    protected override void Configure(HttpClient http, string token)
    {
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", token);
        http.DefaultRequestHeaders.TryAddWithoutValidation("API-Version", ApiVersion);
    }

    /// <inheritdoc/>
    protected override async Task VerifyAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        _board = settings.Get("board");
        _statusColumn = settings.Get("statuscol", "status");
        await QueryAsync("query { me { id } }", new JsonObject(), cancellation);
    }

    // monday répond 200 même en cas d'erreur : elles sont dans « errors » (jeton refusé, colonne inconnue…)
    async Task<JsonNode> QueryAsync(string query, JsonObject variables, CancellationToken cancellation)
    {
        var reply = await SendAsync(HttpMethod.Post, "v2", new JsonObject { ["query"] = query, ["variables"] = variables }, cancellation);
        if (reply["errors"] is JsonArray { Count: > 0 } errors)
            throw new InvalidOperationException($"monday : {errors[0]?["message"] ?? "requête refusée"}");
        if (reply["error_message"] is { } single) throw new InvalidOperationException($"monday : {single}");
        return reply["data"] ?? new JsonObject();
    }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        switch (command.ToLowerInvariant())
        {
            case "item":
            {
                var name = RequireArgument(argument, "le nom de l'élément");
                if (_board.Length == 0) throw new InvalidOperationException("Renseigne le tableau par défaut dans les réglages monday");
                await QueryAsync("mutation ($board: ID!, $name: String!) { create_item (board_id: $board, item_name: $name) { id } }",
                    new JsonObject { ["board"] = _board, ["name"] = name }, cancellation);
                return $"Élément créé : {name}";
            }
            case "status":
            {
                var (item, label) = SplitPair(argument, "identifiant de l'élément=libellé");
                if (_board.Length == 0) throw new InvalidOperationException("Renseigne le tableau par défaut dans les réglages monday");
                await QueryAsync("mutation ($board: ID!, $item: ID!, $column: String!, $value: String!) { change_simple_column_value (board_id: $board, item_id: $item, column_id: $column, value: $value) { id } }",
                    new JsonObject { ["board"] = _board, ["item"] = item, ["column"] = _statusColumn, ["value"] = label }, cancellation);
                return $"Statut « {label} »";
            }
            default: throw new ArgumentException($"Commande monday inconnue : « {command} »");
        }
    }
}
