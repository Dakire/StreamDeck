using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace TouchDeck;

public static class ActionRunner
{
    /// <summary>Ce que les actions de navigation demandent à l'application.</summary>
    public sealed record Hooks(Action<string> GoToPage, Action<string> SwitchProfile, Action<string> OnError);

    public static async Task RunAsync(DeckButton button, Hooks hooks)
    {
        foreach (var action in button.Actions)
        {
            try { await RunOne(action, hooks); }
            catch (Exception ex) { hooks.OnError($"{button.Label} : {ex.Message}"); return; }
        }
    }

    static async Task RunOne(DeckAction a, Hooks hooks)
    {
        switch (a.Type.ToLowerInvariant())
        {
            case "hotkey": NativeInput.Hotkey(a.Value); break;
            case "media": NativeInput.Media(a.Value); break;
            case "text": NativeInput.TypeText(a.Value); break;
            case "page": hooks.GoToPage(a.Value); break;
            case "profile": hooks.SwitchProfile(a.Value); break;
            case "delay": await Task.Delay(int.TryParse(a.Value, out var ms) ? ms : 100); break;
            case "url":
                // Ces protocoles n'ont rien à faire dans un bouton : ils servent à exécuter du code ou lire des fichiers par détour
                if (Uri.TryCreate(a.Value.Trim(), UriKind.Absolute, out var uri)
                    && uri.Scheme.ToLowerInvariant() is "javascript" or "vbscript" or "data" or "file")
                    throw new InvalidOperationException($"Protocole « {uri.Scheme}: » non autorisé");
                Process.Start(new ProcessStartInfo(a.Value.Trim()) { UseShellExecute = true });
                break;
            case "launch":
                Process.Start(new ProcessStartInfo(a.Value) { UseShellExecute = true, Arguments = a.Args ?? "" });
                break;
            case "script": RunScript(a.Value, a.Args); break;
            case "focus":
                if (!WindowFocus.Activate(a.Value))
                {
                    // Fenêtre absente : on lance le programme indiqué (s'il y en a un) et on laisse la main aux actions suivantes
                    if (string.IsNullOrWhiteSpace(a.Args))
                        throw new InvalidOperationException($"Fenêtre « {a.Value} » introuvable");
                    Process.Start(new ProcessStartInfo(a.Args) { UseShellExecute = true });
                    await Task.Delay(1500);
                    WindowFocus.Activate(a.Value);
                }
                break;
            default: throw new ArgumentException($"Type d'action inconnu : « {a.Type} »");
        }
    }

    static void RunScript(string value, string? args)
    {
        var ext = Path.GetExtension(value).ToLowerInvariant();
        var psi = new ProcessStartInfo { CreateNoWindow = true, UseShellExecute = false };
        if (ext == ".ps1")
        {
            psi.FileName = "powershell.exe";
            psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{value}\" {args}";
        }
        else if (ext is ".bat" or ".cmd")
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/c \"\"{value}\" {args}\"";
        }
        else
        {
            // Commande PowerShell en ligne
            psi.FileName = "powershell.exe";
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(value);
        }
        Process.Start(psi);
    }
}
