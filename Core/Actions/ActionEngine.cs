using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Actions;

/// <summary>Issue de l'exécution d'un bouton.</summary>
public enum ActionOutcome
{
    /// <summary>Toutes les étapes ont réussi.</summary>
    Success,
    /// <summary>Une étape a échoué (type inconnu, erreur du système) ; les suivantes n'ont pas été exécutées.</summary>
    Failed,
    /// <summary>Exécution interrompue (fermeture de l'application ou annulation de l'appelant).</summary>
    Cancelled,
    /// <summary>Une étape a dépassé son délai maximal.</summary>
    TimedOut,
}

/// <summary>Résultat de l'exécution d'un bouton. <see cref="Step"/> : étape en échec (0 = première), -1 sinon.</summary>
public sealed record ActionResult(ActionOutcome Outcome, string? Message = null, int Step = -1)
{
    /// <summary>true si toutes les étapes ont réussi.</summary>
    public bool Ok => Outcome == ActionOutcome.Success;
}

/// <summary>Navigation demandée par les actions « page » et « profil » : c'est le deck qui l'applique.</summary>
public interface INavigator
{
    /// <summary>Affiche une page du profil courant : nom de page, <see cref="PageTargets.Home"/> ou <see cref="PageTargets.Back"/>.</summary>
    void GoToPage(string name);
    /// <summary>Affiche le profil indiqué (et l'enregistre comme profil actif).</summary>
    void SwitchProfile(string name);
}

/// <summary>Services partagés par toutes les exécutions.</summary>
public sealed class ActionContext
{
    /// <summary>Clavier, programmes, fenêtres et audio du système (réels ou simulés).</summary>
    public required PlatformServices Platform { get; init; }
    /// <summary>Client HTTP partagé ; le délai de chaque requête est fixé par le moteur, pas par le client.</summary>
    public required HttpClient Http { get; init; }
    /// <summary>Minuteurs en cours, partagés par tous les boutons.</summary>
    public Countdowns Countdowns { get; init; } = new();
    /// <summary>Attente annulable (remplaçable dans les tests pour ne pas attendre réellement).</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
}

/// <summary>Ce qu'une étape connaît de son exécution : le bouton pressé, la navigation et les services.</summary>
public sealed record StepContext(DeckButton Button, INavigator Navigator, ActionContext Services);

/// <summary>Un type d'action (« hotkey », « http »…). Le type est l'identifiant stable écrit dans config.json.</summary>
public interface IActionHandler
{
    /// <summary>Identifiant du type, tel qu'écrit dans config.json (voir <see cref="ActionTypes"/>).</summary>
    string Type { get; }
    /// <summary>Durée maximale d'une étape ; null = sans limite (pause, minuteur).</summary>
    TimeSpan? Timeout { get; }
    /// <summary>Exécute l'étape ; retourne un message d'information à afficher (état du micro…) ou null. Lève une exception en cas d'échec.</summary>
    Task<string?> ExecuteAsync(DeckAction action, StepContext context, CancellationToken cancellation);
}

/// <summary>
/// Exécute les étapes d'un bouton dans l'ordre : chaque étape a un délai maximal, la première en échec arrête la séquence,
/// et tout peut être annulé (fermeture de l'application). Ne dépend ni de WPF ni de Windows.
/// </summary>
public sealed class ActionEngine : IDisposable
{
    readonly ActionRegistry _registry;
    CancellationTokenSource _life = new();

    /// <summary>Services partagés par toutes les exécutions.</summary>
    public ActionContext Context { get; }

    /// <summary>Crée le moteur.</summary>
    /// <param name="context">Services de plateforme, HTTP et minuteurs.</param>
    /// <param name="handlers">Types d'action disponibles ; par défaut <see cref="BuiltInActions.All"/>. Un type en double est refusé.</param>
    public ActionEngine(ActionContext context, IEnumerable<IActionHandler>? handlers = null)
        : this(context, new ActionRegistry(handlers ?? BuiltInActions.All)) { }

    /// <summary>Crée le moteur sur un registre déjà constitué.</summary>
    public ActionEngine(ActionContext context, ActionRegistry registry)
    {
        Context = context;
        _registry = registry;
    }

    /// <summary>Types d'action pris en charge (sans distinction de casse).</summary>
    public IReadOnlyCollection<string> Types => _registry.Types;

    /// <summary>Exécute les étapes du bouton dans l'ordre. Ne lève jamais d'exception : l'échec, l'annulation ou le dépassement de délai sont décrits par le résultat.</summary>
    /// <param name="button">Bouton pressé ; ses étapes sont copiées avant l'exécution.</param>
    /// <param name="navigator">Applique les actions de page et de profil (le deck).</param>
    /// <param name="cancellation">Annulation demandée par l'appelant.</param>
    public async Task<ActionResult> RunAsync(DeckButton button, INavigator navigator, CancellationToken cancellation = default)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(_life.Token, cancellation);
        var ctx = new StepContext(button, navigator, Context);
        string? info = null;
        // Copie : l'éditeur peut modifier le bouton pendant une longue séquence
        var steps = button.Actions.ToList();
        for (int i = 0; i < steps.Count; i++)
        {
            var action = steps[i];
            if (!_registry.TryGet(action.Type, out var handler))
                return new ActionResult(ActionOutcome.Failed, $"Type d'action inconnu : « {action.Type} ». Ouvre le bouton et choisis une autre action.", i);

            using var step = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
            if (handler.Timeout is { } limit) step.CancelAfter(limit);
            try
            {
                run.Token.ThrowIfCancellationRequested();
                info = await handler.ExecuteAsync(action, ctx, step.Token) ?? info;
            }
            catch (OperationCanceledException) when (run.IsCancellationRequested)
            {
                return new ActionResult(ActionOutcome.Cancelled, "Exécution annulée", i);
            }
            catch (OperationCanceledException) when (step.IsCancellationRequested)
            {
                return new ActionResult(ActionOutcome.TimedOut, $"Délai dépassé ({handler.Timeout!.Value.TotalSeconds:0} s) : l'action n'a pas répondu. Réessaie, ou vérifie son adresse ou son programme.", i);
            }
            catch (Exception ex)
            {
                return new ActionResult(ActionOutcome.Failed, ex.Message, i);
            }
        }
        return new ActionResult(ActionOutcome.Success, info);
    }

    /// <summary>Interrompt toutes les exécutions en cours (les suivantes fonctionnent normalement).</summary>
    public void CancelAll()
    {
        var old = _life;
        _life = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();
    }

    /// <summary>Annule les exécutions en cours ; le moteur n'est plus utilisable ensuite (arrêt de l'application).</summary>
    public void Dispose()
    {
        _life.Cancel();
        _life.Dispose();
    }
}
