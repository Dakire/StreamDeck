using System;
using System.IO;

namespace TouchDeck;

/// <summary>
/// Journal de diagnostic dans le dossier de configuration (touchdeck.log, 1 Mo maximum, l'ancien est gardé en .old).
/// Ne lève jamais d'exception : un journal en échec ne doit pas faire tomber le deck.
/// </summary>
public static class Log
{
    public const long MaxBytes = 1_000_000;
    static readonly object Gate = new();

    public static string FilePath => Path.Combine(ConfigStore.Dir, "touchdeck.log");

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex == null ? message : $"{message}\n{ex}");

    static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var path = FilePath;
                Directory.CreateDirectory(ConfigStore.Dir);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes) File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch { /* journal best-effort */ }
    }
}
