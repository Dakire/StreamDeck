using System.Collections.Generic;
using System.Linq;

namespace StreamDak.Integrations;

/// <summary>Actions du catalogue d'une intégration : une entrée prête à poser par commande.</summary>
public static class IntegrationCatalog
{
    /// <summary>Identifiant stable de l'entrée de catalogue d'une commande (« obs.scene »).</summary>
    public static string EntryId(IIntegration integration, IntegrationCommand command) => $"{integration.Id}.{command.Name}";

    /// <summary>Une entrée par commande, dans la catégorie de l'intégration.</summary>
    public static IReadOnlyList<CatalogAction> Entries(IIntegration integration) => integration.Commands
        .Select(c => new CatalogAction(
            EntryId(integration, c), integration.Category, c.Label, c.Description, c.Icon, c.Color,
            new[] { new DeckAction { Type = integration.Id, Value = c.Name } },
            c.ArgHint is { } hint ? $"{(c.ArgRequired ? "Obligatoire" : "Facultatif")} : {hint}." : null,
            false, $"{integration.Name} {integration.Id}"))
        .ToList();
}