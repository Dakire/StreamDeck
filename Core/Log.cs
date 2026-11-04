using System;

namespace StreamDak;

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

    /// <summary>Écrit sur disque les messages en attente (arrêt brutal). Sans effet pour une destination synchrone.</summary>
    void Flush() { }
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

    /// <summary>Vide la file d'écriture : à appeler quand le processus va mourir (erreur fatale), sinon les dernières lignes sont perdues.</summary>
    public static void Flush()
    {
        try { Sink.Flush(); }
        catch (Exception) { /* un journal en échec ne doit jamais faire tomber l'application */ }
    }

    const int RecentMax = 30;
    static readonly System.Collections.Generic.Queue<string> RecentLines = new();

    /// <summary>Derniers avertissements et erreurs (30 au plus, du plus ancien au plus récent), pour le diagnostic copiable. Passés par <see cref="LogRedactor"/>.</summary>
    public static System.Collections.Generic.IReadOnlyList<string> Recent()
    {
        lock (RecentLines) return RecentLines.ToArray();
    }

    static void Write(LogSeverity severity, string message, Exception? exception)
    {
        if (severity != LogSeverity.Info)
            lock (RecentLines)
            {
                RecentLines.Enqueue($"{DateTime.Now:HH:mm:ss} {(severity == LogSeverity.Error ? "ERR" : "WRN")} {LogRedactor.Redact(message)}{(exception != null ? " (" + exception.GetType().Name + ")" : "")}");
                while (RecentLines.Count > RecentMax) RecentLines.Dequeue();
            }
        try { Sink.Write(severity, message, exception); }
        catch (Exception) { /* un journal en échec ne doit jamais faire tomber l'application */ }
    }
}
