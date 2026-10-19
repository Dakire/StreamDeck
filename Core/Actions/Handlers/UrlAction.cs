using System;
using System.Linq;

namespace TouchDeck.Actions;

/// <summary>Ouvre une adresse avec l'application associée, sauf les protocoles qui permettent d'exécuter du code ou de lire des fichiers.</summary>
public sealed class UrlAction : BackgroundActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Url"/>.</summary>
    public UrlAction() : base(ActionTypes.Url, ActionTimeouts.Launch) { }

    /// <inheritdoc/>
    protected override void Run(DeckAction action, StepContext context) =>
        context.Services.Platform.Launcher.Open(Check(action.Value), null);

    /// <summary>Ces protocoles n'ont rien à faire dans un bouton : ils servent à exécuter du code ou lire des fichiers par détour.</summary>
    static string Check(string value)
    {
        var v = value.Trim();
        if (Uri.TryCreate(v, UriKind.Absolute, out var uri) && ActionValidator.BlockedSchemes.Contains(uri.Scheme.ToLowerInvariant()))
            throw new InvalidOperationException($"Protocole « {uri.Scheme}: » non autorisé");
        return v;
    }
}