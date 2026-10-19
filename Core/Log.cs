using System;

namespace TouchDeck;

/// <summary>Gravité d'un message du journal.</summary>
public enum LogSeverity
{
    /// <summary>Événement normal (démarrage, changement d'écran).</summary>
    Info,
    /// <summary>Anomalie sans conséquence grave (action en échec, image illisible).</summary>
    Warning,
    /// <summary>Erreur : quelque chose n'a pas pu être fait.</summary>
    Error,
}

/// <summary>Destination des messages du journal ; l'application branche Serilog, les tests une liste en mémoire.</summary>
public interface ILogSink
{
    /// <summary>Consigne un message. Ne doit jamais lever d'exception : un journal en échec ne doit pas faire tomber le deck.</summary>
    void Write(LogSeverity severity, string message, Exception? exception);
}

/// <summary>
/// Façade du journal pour le code qui n'est pas construit par l'injection de dépendances (persistance, validation, plateforme).
/// Le Core ne dépend d'aucune bibliothèque de journalisation : la racine de composition branche <see cref="Sink"/>.
/// Sans destination, les messages sont ignorés.
/// </summary>
public static class Log
{
    sealed class NullSink : ILogSink
    {
        public void Write(LogSeverity severity, string message, Exception? exception) { }
    }

    /// <summary>Destination courante des messages.</summary>
    public static ILogSink Sink { get; set; } = new NullSink();

    /// <summary>Événement normal (démarrage, changement d'écran).</summary>
    public static void Info(string message) => Write(LogSeverity.Info, message, null);
    /// <summary>Anomalie sans conséquence grave (action en échec, image illisible).</summary>
    public static void Warn(string message) => Write(LogSeverity.Warning, message, null);
    /// <summary>Erreur, avec l'exception complète si elle est fournie.</summary>
    public static void Error(string message, Exception? ex = null) => Write(LogSeverity.Error, message, ex);

    static void Write(LogSeverity severity, string message, Exception? exception)
    {
        try { Sink.Write(severity, message, exception); }
        catch (Exception) { /* un journal en échec ne doit jamais faire tomber l'application */ }
    }
}
