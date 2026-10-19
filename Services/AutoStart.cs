using System;
using Microsoft.Win32;

namespace TouchDeck;

/// <summary>Lancement à l'ouverture de session Windows, via la clé Run de l'utilisateur (aucun droit administrateur).</summary>
public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "TouchDeck";

    /// <summary>true si TouchDeck est inscrit dans la clé Run de l'utilisateur (le registre fait foi, pas config.json).</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            Log.Warn("Lecture du lancement automatique impossible : " + ex.Message);
            return false;
        }
    }

    /// <summary>Active ou désactive. Retourne false si le registre refuse l'écriture.</summary>
    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                key.SetValue(ValueName, "\"" + exe + "\"");
            }
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            Log.Warn("Écriture du lancement automatique refusée : " + ex.Message);   // l'appelant prévient l'utilisateur
            return false;
        }
    }
}
