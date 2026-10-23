using System;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Actions;

/// <summary>Délais maximaux des étapes (une étape qui dépasse son délai est interrompue : le bouton affiche un échec).</summary>
public static class ActionTimeouts
{
    /// <summary>Clavier, audio : opérations instantanées.</summary>
    public static readonly TimeSpan Quick = TimeSpan.FromSeconds(5);
    /// <summary>Lancement d'un programme, d'un script ou d'une adresse (disque, antivirus).</summary>
    public static readonly TimeSpan Launch = TimeSpan.FromSeconds(15);
    /// <summary>Requête web (l'éditeur l'indique à l'utilisateur).</summary>
    public static readonly TimeSpan Http = TimeSpan.FromSeconds(10);
    /// <summary>Mise au premier plan d'une fenêtre, avec lancement éventuel du programme.</summary>
    public static readonly TimeSpan Focus = TimeSpan.FromSeconds(10);
}

/// <summary>Base d'un type d'action : identifiant stable (clé de config.json) et délai maximal.</summary>
public abstract class ActionHandler : IActionHandler
{
    /// <summary>Crée le gestionnaire du type <paramref name="type"/> ; <paramref name="timeout"/> null : pas de limite (pause, minuteur).</summary>
    protected ActionHandler(string type, TimeSpan? timeout)
    {
        Type = type;
        Timeout = timeout;
    }

    /// <inheritdoc/>
    public string Type { get; }

    /// <inheritdoc/>
    public TimeSpan? Timeout { get; }

    /// <inheritdoc/>
    public abstract Task<string?> ExecuteAsync(DeckAction action, StepContext context, CancellationToken cancellation);
}

/// <summary>Action instantanée, exécutée sur place (clavier, navigation, audio, minuteur).</summary>
public abstract class SyncActionHandler : ActionHandler
{
    /// <summary>Crée le gestionnaire du type <paramref name="type"/> ; <paramref name="timeout"/> null : pas de limite.</summary>
    protected SyncActionHandler(string type, TimeSpan? timeout) : base(type, timeout) { }

    /// <summary>Exécute l'étape ; retourne un message à afficher (état du micro…) ou null. Lève une exception en cas d'échec.</summary>
    protected abstract string? Run(DeckAction action, StepContext context);

    /// <inheritdoc/>
    public override Task<string?> ExecuteAsync(DeckAction action, StepContext context, CancellationToken cancellation) =>
        Task.FromResult(Run(action, context));
}

/// <summary>
/// Action qui peut bloquer (disque, antivirus, programme lent) : exécutée hors du thread d'interface, avec un délai maximal
/// et annulable.
/// </summary>
public abstract class BackgroundActionHandler : ActionHandler
{
    /// <summary>Crée le gestionnaire du type <paramref name="type"/> avec le délai maximal <paramref name="timeout"/>.</summary>
    protected BackgroundActionHandler(string type, TimeSpan timeout) : base(type, timeout) { }

    /// <summary>Exécute l'étape sur le pool de threads. Lève une exception en cas d'échec.</summary>
    protected abstract void Run(DeckAction action, StepContext context);

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(DeckAction action, StepContext context, CancellationToken cancellation)
    {
        await Task.Run(() => Run(action, context), cancellation).WaitAsync(cancellation);
        return null;
    }
}