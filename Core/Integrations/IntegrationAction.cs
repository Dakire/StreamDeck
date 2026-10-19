using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TouchDeck.Actions;

namespace TouchDeck.Integrations;

/// <summary>
/// Type d'action d'une intégration : <c>Type</c> = identifiant de l'intégration, <c>Value</c> = commande, <c>Args</c> =
/// argument. Aucun nouveau format de configuration : une étape OBS s'écrit <c>{ "Type": "obs", "Value": "scene", "Args": "Jeu" }</c>.
/// </summary>
public sealed class IntegrationAction : ActionHandler
{
    readonly IIntegration _integration;

    /// <summary>Gestionnaire du type <c>integration.Id</c>.</summary>
    public IntegrationAction(IIntegration integration) : base(integration.Id, ActionTimeouts.Http) { _integration = integration; }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(DeckAction action, StepContext context, CancellationToken cancellation)
    {
        if (_integration.State != ConnectionState.Connected)
            throw new InvalidOperationException($"{_integration.Name} n'est pas connecté" + (_integration.Error is { } e ? $" ({e})" : ""));
        return await _integration.ExecuteAsync(action.Value, action.Args, cancellation);
    }

    /// <summary>Problèmes d'une étape de cette intégration (commande inconnue, argument manquant), sans rien exécuter.</summary>
    public static IEnumerable<Issue> Validate(IIntegration integration, DeckAction action)
    {
        var command = integration.Commands.FirstOrDefault(c => c.Name.Equals(action.Value, StringComparison.OrdinalIgnoreCase));
        if (command == null) { yield return new Issue(Severity.Error, "Choisis une commande dans la liste"); yield break; }
        if (command.ArgRequired && string.IsNullOrWhiteSpace(action.Args))
            yield return new Issue(Severity.Error, $"Indique {command.ArgHint ?? "l'argument"}");
    }
}