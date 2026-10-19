using System.Collections.Generic;

namespace TouchDeck;

/// <summary>Modèle de bouton proposé à la création : libellé, icône, couleur et actions pré-remplies.</summary>
public record ButtonTemplate(string Group, string Name, string Icon, string Color, DeckAction[] Actions, string? Note = null);

public static class Templates
{
    static DeckAction A(string type, string value, string? args = null) => new() { Type = type, Value = value, Args = args };

    // Ramène Chrome au premier plan (le lance s'il est fermé), laisse 150 ms à Windows, puis envoie le raccourci
    static DeckAction[] InChrome(string hotkey) => new[]
    {
        A("focus", "chrome", "chrome.exe"), A("delay", "150"), A("hotkey", hotkey),
    };

    public static readonly ButtonTemplate[] All =
    {
        // --- Google Chrome ---
        new("Chrome", "Chrome", "🌐", "#3466D6", new[] { A("focus", "chrome", "chrome.exe") },
            "Ramène Chrome au premier plan, ou le lance s'il est fermé."),
        new("Chrome", "Nouvel onglet", "➕", "#3466D6", InChrome("ctrl+t")),
        new("Chrome", "Rouvrir l'onglet", "↩️", "#3466D6", InChrome("ctrl+shift+t")),
        new("Chrome", "Fermer l'onglet", "✖️", "#B23A48", InChrome("ctrl+w")),
        new("Chrome", "Onglet suivant", "➡️", "#272B38", InChrome("ctrl+tab")),
        new("Chrome", "Onglet précédent", "⬅️", "#272B38", InChrome("ctrl+shift+tab")),
        new("Chrome", "Actualiser", "🔄", "#272B38", InChrome("ctrl+r")),
        new("Chrome", "Navigation privée", "🕶️", "#6C3FD1", InChrome("ctrl+shift+n")),

        // --- Google (ouverts dans le navigateur par défaut) ---
        new("Google", "Google", "🔍", "#3466D6", new[] { A("url", "https://www.google.com") }),
        new("Google", "Gmail", "✉️", "#B23A48", new[] { A("url", "https://mail.google.com") }),
        new("Google", "Drive", "📁", "#1E7F5F", new[] { A("url", "https://drive.google.com") }),
        new("Google", "Agenda", "📅", "#3466D6", new[] { A("url", "https://calendar.google.com") }),
        new("Google", "Meet", "🎥", "#1E7F5F", new[] { A("url", "https://meet.google.com") }),
        new("Google", "Docs", "📄", "#3466D6", new[] { A("url", "https://docs.google.com") }),
        new("Google", "Maps", "🗺️", "#B7791F", new[] { A("url", "https://maps.google.com") }),

        // --- 3CX ---
        new("3CX", "3CX", "📞", "#1E7F5F", new[] { A("focus", "3CX", "") },
            "Ramène 3CX au premier plan (recherche « 3CX » dans le titre de la fenêtre). Pour le lancer s'il est fermé, indique son chemin dans « Si absente, lancer »."),
        new("3CX", "Appeler", "☎️", "#1E7F5F", new[] { A("url", "tel:+33") },
            "Remplace +33 par le numéro à appeler. Fonctionne si 3CX est l'application d'appel par défaut de Windows (Paramètres › Applications par défaut › TEL)."),
        new("3CX", "Raccourci 3CX", "🎧", "#1E7F5F", new[] { A("focus", "3CX", ""), A("delay", "150"), A("hotkey", "") },
            "Ramène 3CX au premier plan puis envoie un raccourci. Saisis dans « Combinaison » le raccourci de ta version de 3CX (décrocher, raccrocher, couper le micro…)."),

        // --- Keeper ---
        new("Keeper", "Coffre Keeper", "🔐", "#6C3FD1", new[] { A("url", "https://keepersecurity.com/vault") },
            "Ouvre le coffre web. Si ton compte est en Europe, remplace keepersecurity.com par keepersecurity.eu."),
        new("Keeper", "Keeper : remplir", "🔑", "#6C3FD1", new[] { A("focus", "chrome", "chrome.exe"), A("delay", "150"), A("hotkey", "") },
            "Définis d'abord le raccourci de l'extension Keeper dans chrome://extensions/shortcuts, puis saisis-le dans « Combinaison ». Ne stocke jamais de mot de passe dans un bouton : il serait écrit en clair dans la configuration."),
        new("Keeper", "Application Keeper", "🗝️", "#6C3FD1", new[] { A("focus", "Keeper", "") },
            "Ramène l'application Keeper au premier plan si elle est installée."),
    };
}
