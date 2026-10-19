using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Integrations.Notion;

/// <summary>
/// Notion par son API (https://developers.notion.com/reference) : ajouter une ligne à une base de données et changer le statut d'une page.
/// Jeton d'une intégration interne ; l'intégration doit être connectée à la base (menu « ⋯ › Connexions »). Notion limite à
/// environ 3 requêtes par seconde : au-delà, le délai à attendre est affiché.
/// </summary>
public sealed class NotionIntegration : RestIntegrationBase
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "notion";
    string _database = "", _titleProperty = "Name", _statusProperty = "Status";

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "Notion";
    /// <inheritdoc/>
    public override string Category => "Notion";
    /// <inheritdoc/>
    public override string Icon => "Notebook24";
    /// <inheritdoc/>
    public override string Help => "Crée une intégration interne sur notion.so/my-integrations, copie son jeton ici, puis connecte-la à ta base de données (⋯ › Connexions). L'identifiant de la base est le long nombre de son adresse.";
    /// <inheritdoc/>
    protected override string DefaultBaseUrl => "https://api.notion.com/v1";

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("token", "Jeton de l'intégration", SettingKind.Secret, null, "Commence par ntn_ ou secret_."),
        new SettingField("database", "Base de données", SettingKind.Text, null, "Identifiant de la base où ajouter des lignes."),
        new SettingField("titleprop", "Propriété du titre", SettingKind.Text, "Name", "Nom de la colonne titre de la base (« Nom » dans une base en français)."),
        new SettingField("statusprop", "Propriété du statut", SettingKind.Text, "Status", "Nom de la colonne de type Statut."),
    };

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("page", "Nouvelle ligne", "Ajoute une ligne à la base de données.", "le titre de la ligne", true, null, "DocumentAdd24", "#334155"),
        new IntegrationCommand("status", "Changer le statut", "Change la propriété Statut d'une page.", "« identifiant de la page=statut »", true, null, "CheckmarkCircle24", "#334155"),
    };

    /// <inheritdoc/>
    protected override void Configure(HttpClient http, string token)
    {
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + token);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Notion-Version", "2022-06-28");
    }

    /// <inheritdoc/>
    protected override async Task VerifyAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        _database = settings.Get("database");
        _titleProperty = settings.Get("titleprop", "Name");
        _statusProperty = settings.Get("statusprop", "Status");
        await SendAsync(HttpMethod.Get, "users/me", null, cancellation);
    }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        switch (command.ToLowerInvariant())
        {
            case "page":
            {
                var title = RequireArgument(argument, "le titre de la ligne");
                if (_database.Length == 0) throw new InvalidOperationException("Renseigne la base de données dans les réglages Notion");
                var body = new JsonObject
                {
                    ["parent"] = new JsonObject { ["database_id"] = _database },
                    ["properties"] = new JsonObject
                    {
                        [_titleProperty] = new JsonObject { ["title"] = new JsonArray(new JsonObject { ["text"] = new JsonObject { ["content"] = title } }) },
                    },
                };
                await SendAsync(HttpMethod.Post, "pages", body, cancellation);
                return $"Ligne ajoutée : {title}";
            }
            case "status":
            {
                var (page, status) = SplitPair(argument, "identifiant de la page=statut");
                var body = new JsonObject
                {
                    ["properties"] = new JsonObject { [_statusProperty] = new JsonObject { ["status"] = new JsonObject { ["name"] = status } } },
                };
                await SendAsync(HttpMethod.Patch, $"pages/{Uri.EscapeDataString(page)}", body, cancellation);
                return $"Statut « {status} »";
            }
            default: throw new ArgumentException($"Commande Notion inconnue : « {command} »");
        }
    }
}
