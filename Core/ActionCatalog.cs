using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchDeck;

/// <summary>Une entrée du catalogue : un bouton prêt à poser, avec ses actions pré-remplies.</summary>
public sealed record CatalogAction(
    string Id, string Category, string Name, string Description, string Icon, string Color,
    DeckAction[] Actions, string? Note = null, bool IsFolder = false, string Keywords = "", string? Label = null)
{
    /// <summary>Titre du bouton créé (par défaut le nom ; « {time} » pour l'horloge).</summary>
    public string ButtonLabel => Label ?? Name;

    /// <summary>Bouton neuf (copie indépendante du catalogue).</summary>
    public DeckButton CreateButton()
    {
        if (IsFolder) return DeckDocument.NewFolder(Name);
        return new DeckButton
        {
            Label = ButtonLabel, Icon = Icon, Color = Color,
            Actions = Actions.Select(a => new DeckAction { Type = a.Type, Value = a.Value, Args = a.Args }).ToList(),
        };
    }
}

/// <summary>Catalogue d'actions façon Stream Deck : catégories, recherche, favoris. Les identifiants sont stables (stockés dans la config).</summary>
public static class ActionCatalog
{
    public const string SystemCategory = "Système";
    public const string Apps = "Applications";
    public const string Multimedia = "Multimédia";
    public const string Audio = "Audio";
    public const string Keyboard = "Raccourcis clavier";
    public const string Web = "Sites web";
    public const string Automation = "Automatisation";
    public const string Navigation = "Profils et navigation";

    public static readonly string[] Categories = { SystemCategory, Apps, Multimedia, Audio, Keyboard, Web, Automation, Navigation };

    public static readonly Dictionary<string, string> CategoryIcons = new()
    {
        [SystemCategory] = "Desktop24", [Apps] = "Apps24", [Multimedia] = "Play24", [Audio] = "Speaker224",
        [Keyboard] = "Keyboard24", [Web] = "Globe24", [Automation] = "Flash24", [Navigation] = "Navigation24",
    };

    static DeckAction A(string type, string value, string? args = null) => new() { Type = type, Value = value, Args = args };

    // Ramène Chrome au premier plan (le lance s'il est fermé), laisse 150 ms à Windows, puis envoie le raccourci
    static DeckAction[] InChrome(string hotkey) => new[]
    {
        A("focus", "chrome", "chrome.exe"), A("delay", "150"), A("hotkey", hotkey),
    };

    static CatalogAction C(string id, string cat, string name, string desc, string icon, string color, DeckAction[] actions,
        string? note = null, string keywords = "") => new(id, cat, name, desc, icon, color, actions, note, false, keywords);

    public static readonly IReadOnlyList<CatalogAction> All = new[]
    {
        // --- Système ---
        C("sys.desktop", SystemCategory, "Bureau", "Affiche ou masque le bureau (Win+D).", "Desktop24", "#2563EB", new[] { A("hotkey", "win+d") }),
        C("sys.explorer", SystemCategory, "Explorateur", "Ouvre l'Explorateur de fichiers (Win+E).", "Folder24", "#334155", new[] { A("hotkey", "win+e") }, null, "fichiers"),
        C("sys.taskmgr", SystemCategory, "Gestionnaire des tâches", "Ouvre le Gestionnaire des tâches (Ctrl+Maj+Échap).", "DataBarVertical24", "#2563EB", new[] { A("hotkey", "ctrl+shift+esc") }, null, "processus"),
        C("sys.snip", SystemCategory, "Capture d'écran", "Outil de capture (Win+Maj+S).", "Cut24", "#2563EB", new[] { A("hotkey", "win+shift+s") }, null, "screenshot"),
        C("sys.lock", SystemCategory, "Verrouiller la session", "Verrouille Windows (Win+L).", "LockClosed24", "#BE123C", new[] { A("hotkey", "win+l") }),
        C("sys.run", SystemCategory, "Exécuter", "Ouvre la boîte Exécuter (Win+R).", "Rocket24", "#334155", new[] { A("hotkey", "win+r") }),
        C("sys.settings", SystemCategory, "Paramètres Windows", "Ouvre les Paramètres (Win+I).", "Settings24", "#334155", new[] { A("hotkey", "win+i") }),
        C("sys.search", SystemCategory, "Recherche Windows", "Ouvre la recherche (Win+S).", "Search24", "#334155", new[] { A("hotkey", "win+s") }),
        C("sys.switch", SystemCategory, "Changer de fenêtre", "Alt+Tab.", "Window24", "#334155", new[] { A("hotkey", "alt+tab") }, null, "alt tab"),
        new("sys.clock", SystemCategory, "Horloge", "Affiche l'heure en direct.", "Clock24", "#334155", Array.Empty<DeckAction>(),
            "Le titre « {time} » affiche l'heure ; « {time:HH:mm:ss} » ajoute les secondes. Ces modèles marchent dans n'importe quel titre.",
            false, "heure temps", "{time}"),
        new("sys.date", SystemCategory, "Date", "Affiche la date du jour.", "Calendar24", "#334155", Array.Empty<DeckAction>(),
            "Le titre « {date:dddd d MMMM} » affiche par exemple « lundi 19 octobre » (formats de date .NET).", false, "jour calendrier", "{date:dddd d MMMM}"),

        // --- Applications ---
        C("app.launch", Apps, "Ouvrir un programme", "Lance un programme ou un dossier de ton choix.", "Rocket24", "#7C3AED", new[] { A("launch", "") }),
        C("app.focus", Apps, "Fenêtre au premier plan", "Ramène une fenêtre au premier plan (et lance le programme si besoin).", "Window24", "#7C3AED", new[] { A("focus", "") }),
        C("app.notepad", Apps, "Bloc-notes", "Lance le Bloc-notes.", "Notepad24", "#334155", new[] { A("launch", "notepad.exe") }),
        C("app.calc", Apps, "Calculatrice", "Lance la Calculatrice.", "Calculator24", "#334155", new[] { A("launch", "calc.exe") }),
        C("chrome.focus", Apps, "Chrome", "Ramène Chrome au premier plan, ou le lance s'il est fermé.", "Globe24", "#2563EB",
            new[] { A("focus", "chrome", "chrome.exe") }, "Ramène Chrome au premier plan, ou le lance s'il est fermé.", "navigateur google"),
        C("chrome.newtab", Apps, "Chrome : nouvel onglet", "Ctrl+T dans Chrome.", "Add24", "#2563EB", InChrome("ctrl+t")),
        C("chrome.reopen", Apps, "Chrome : rouvrir l'onglet", "Ctrl+Maj+T dans Chrome.", "ArrowUndo24", "#2563EB", InChrome("ctrl+shift+t")),
        C("chrome.close", Apps, "Chrome : fermer l'onglet", "Ctrl+W dans Chrome.", "Dismiss24", "#BE123C", InChrome("ctrl+w")),
        C("chrome.next", Apps, "Chrome : onglet suivant", "Ctrl+Tab dans Chrome.", "ArrowRight24", "#334155", InChrome("ctrl+tab")),
        C("chrome.prev", Apps, "Chrome : onglet précédent", "Ctrl+Maj+Tab dans Chrome.", "ArrowLeft24", "#334155", InChrome("ctrl+shift+tab")),
        C("chrome.reload", Apps, "Chrome : actualiser", "Ctrl+R dans Chrome.", "ArrowSync24", "#334155", InChrome("ctrl+r")),
        C("chrome.private", Apps, "Chrome : navigation privée", "Ctrl+Maj+N dans Chrome.", "Eye24", "#7C3AED", InChrome("ctrl+shift+n")),
        C("3cx.focus", Apps, "3CX", "Ramène 3CX au premier plan.", "Call24", "#047857", new[] { A("focus", "3CX", "") },
            "Recherche « 3CX » dans le titre de la fenêtre. Pour le lancer s'il est fermé, indique son chemin dans « Si absente, lancer ».", "téléphone"),
        C("3cx.call", Apps, "3CX : appeler", "Compose un numéro (lien tel:).", "Call24", "#047857", new[] { A("url", "tel:+33") },
            "Remplace +33 par le numéro à appeler. Fonctionne si 3CX est l'application d'appel par défaut de Windows (Paramètres › Applications par défaut › TEL)."),
        C("3cx.hotkey", Apps, "3CX : raccourci", "Ramène 3CX au premier plan puis envoie un raccourci.", "Headphones24", "#047857",
            new[] { A("focus", "3CX", ""), A("delay", "150"), A("hotkey", "") },
            "Saisis dans « Combinaison » le raccourci de ta version de 3CX (décrocher, raccrocher, couper le micro…)."),
        C("keeper.vault", Apps, "Keeper : coffre web", "Ouvre le coffre Keeper dans le navigateur.", "LockClosed24", "#7C3AED",
            new[] { A("url", "https://keepersecurity.com/vault") },
            "Si ton compte est en Europe, remplace keepersecurity.com par keepersecurity.eu.", "mot de passe"),
        C("keeper.fill", Apps, "Keeper : remplir", "Envoie le raccourci de l'extension Keeper à Chrome.", "Key24", "#7C3AED",
            new[] { A("focus", "chrome", "chrome.exe"), A("delay", "150"), A("hotkey", "") },
            "Définis d'abord le raccourci de l'extension Keeper dans chrome://extensions/shortcuts, puis saisis-le dans « Combinaison ». Ne stocke jamais de mot de passe dans un bouton : il serait écrit en clair dans la configuration."),
        C("keeper.app", Apps, "Keeper : application", "Ramène l'application Keeper au premier plan.", "Key24", "#7C3AED", new[] { A("focus", "Keeper", "") }),

        // --- Multimédia ---
        C("media.playpause", Multimedia, "Lecture / Pause", "Touche média Lecture/Pause.", "Play24", "#047857", new[] { A("media", "playpause") }),
        C("media.prev", Multimedia, "Précédent", "Piste précédente.", "Previous24", "#334155", new[] { A("media", "prev") }),
        C("media.next", Multimedia, "Suivant", "Piste suivante.", "Next24", "#334155", new[] { A("media", "next") }),
        C("media.stop", Multimedia, "Stop", "Arrête la lecture.", "Stop24", "#334155", new[] { A("media", "stop") }),

        // --- Audio ---
        C("audio.mute", Audio, "Muet", "Coupe ou rétablit le son.", "SpeakerMute24", "#BE123C", new[] { A("media", "mute") }),
        C("audio.down", Audio, "Volume -", "Baisse le volume.", "Speaker124", "#334155", new[] { A("media", "voldown") }),
        C("audio.up", Audio, "Volume +", "Augmente le volume.", "Speaker224", "#334155", new[] { A("media", "volup") }),
        C("audio.mic", Audio, "Micro", "Coupe ou rétablit le micro par défaut.", "Mic24", "#BE123C", new[] { A("audio", "mic-toggle") },
            "Agit sur le micro de communication par défaut de Windows ; l'état s'affiche dans la barre du deck.", "microphone muet visio"),
        C("audio.level", Audio, "Volume 50 %", "Règle le volume de sortie à un niveau précis.", "Speaker224", "#334155", new[] { A("audio", "volume", "50") },
            "Change le niveau dans « Volume (0 à 100) ».", "niveau son"),

        // --- Raccourcis clavier ---
        C("key.hotkey", Keyboard, "Raccourci clavier", "Envoie une combinaison de touches.", "Keyboard24", "#334155", new[] { A("hotkey", "") }),
        C("key.text", Keyboard, "Saisie de texte", "Tape un texte dans l'application active.", "TextT24", "#334155", new[] { A("text", "") }),
        C("key.copy", Keyboard, "Copier", "Ctrl+C.", "Copy24", "#334155", new[] { A("hotkey", "ctrl+c") }),
        C("key.paste", Keyboard, "Coller", "Ctrl+V.", "ClipboardPaste24", "#334155", new[] { A("hotkey", "ctrl+v") }),
        C("key.cut", Keyboard, "Couper", "Ctrl+X.", "Cut24", "#334155", new[] { A("hotkey", "ctrl+x") }),
        C("key.undo", Keyboard, "Annuler", "Ctrl+Z.", "ArrowUndo24", "#334155", new[] { A("hotkey", "ctrl+z") }),
        C("key.redo", Keyboard, "Rétablir", "Ctrl+Y.", "ArrowRedo24", "#334155", new[] { A("hotkey", "ctrl+y") }),
        C("key.save", Keyboard, "Enregistrer", "Ctrl+S.", "Save24", "#334155", new[] { A("hotkey", "ctrl+s") }),

        // --- Sites web ---
        C("web.url", Web, "Ouvrir un site web", "Ouvre l'adresse de ton choix dans le navigateur par défaut.", "Link24", "#2563EB", new[] { A("url", "https://") }),
        C("web.google", Web, "Google", "google.com", "Search24", "#2563EB", new[] { A("url", "https://www.google.com") }),
        C("web.gmail", Web, "Gmail", "mail.google.com", "Mail24", "#BE123C", new[] { A("url", "https://mail.google.com") }),
        C("web.drive", Web, "Drive", "drive.google.com", "Folder24", "#047857", new[] { A("url", "https://drive.google.com") }),
        C("web.calendar", Web, "Agenda", "calendar.google.com", "Calendar24", "#2563EB", new[] { A("url", "https://calendar.google.com") }),
        C("web.meet", Web, "Meet", "meet.google.com", "Video24", "#047857", new[] { A("url", "https://meet.google.com") }),
        C("web.docs", Web, "Docs", "docs.google.com", "Document24", "#2563EB", new[] { A("url", "https://docs.google.com") }),
        C("web.maps", Web, "Maps", "maps.google.com", "Map24", "#B45309", new[] { A("url", "https://maps.google.com") }),

        // --- Automatisation ---
        C("auto.script", Automation, "Script PowerShell", "Exécute une commande PowerShell ou un fichier .ps1 / .bat / .cmd.", "Flash24", "#7C3AED", new[] { A("script", "") }),
        C("auto.multi", Automation, "Multi-actions", "Enchaîne plusieurs étapes : programme, pause, raccourci, lien…", "Layer24", "#7C3AED",
            new[] { A("launch", ""), A("delay", "500"), A("hotkey", "") }, "Ajoute, réordonne et supprime les étapes dans le panneau de propriétés.", "macro séquence"),
        C("auto.delay", Automation, "Pause", "Attend avant l'étape suivante (à utiliser dans un enchaînement).", "Timer24", "#334155", new[] { A("delay", "500") }),
        C("auto.webhook", Automation, "Requête web", "Appelle une adresse (webhook, domotique, API) : GET, ou POST avec un corps.", "Send24", "#7C3AED",
            new[] { A("http", "https://") }, "Le corps (facultatif) est envoyé en JSON s'il commence par { ou [. Délai maximal : 10 secondes.", "webhook api http post domotique"),
        C("auto.timer", Automation, "Minuteur", "Compte à rebours affiché sur le bouton ; toucher à nouveau l'arrête.", "Timer24", "#B45309",
            new[] { A("timer", "300") }, "Durée en secondes (300 = 5 minutes). Un son et un message signalent la fin.", "compte à rebours chrono"),

        // --- Profils et navigation ---
        new("nav.folder", Navigation, "Dossier", "Regroupe des boutons dans une grille imbriquée.", "Folder24", "#B45309", Array.Empty<DeckAction>(), null, true, "sous-dossier"),
        C("nav.page", Navigation, "Aller à une page", "Affiche une autre page du profil.", "Document24", "#B45309", new[] { A("page", "home") }),
        C("nav.home", Navigation, "Accueil", "Retourne à la première page.", "Home24", "#B45309", new[] { A("page", "home") }),
        C("nav.back", Navigation, "Retour", "Retourne à la page précédente.", "ArrowLeft24", "#B45309", new[] { A("page", "back") }),
        C("nav.profile", Navigation, "Changer de profil", "Affiche un autre profil sur le deck.", "PersonSwap24", "#7C3AED", new[] { A("profile", "") }),
    };

    static readonly Dictionary<string, CatalogAction> ById = All.ToDictionary(a => a.Id);

    public static CatalogAction? Find(string? id) => id != null && ById.TryGetValue(id, out var a) ? a : null;

    /// <summary>Recherche instantanée : tous les mots doivent figurer dans le nom, la description, la catégorie ou les mots-clés (sans accents ni casse).</summary>
    public static IEnumerable<CatalogAction> Search(string? query, IEnumerable<CatalogAction>? source = null)
    {
        source ??= All;
        var words = Fold(query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return source;
        return source.Where(a =>
        {
            var hay = Fold($"{a.Name} {a.Description} {a.Category} {a.Keywords}");
            return words.All(hay.Contains);
        });
    }

    public static string Fold(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (var c in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>Retrouve l'entrée du catalogue d'où vient un bouton (même libellé et mêmes actions), pour les infos et favoris.</summary>
    public static CatalogAction? Identify(DeckButton b) =>
        All.FirstOrDefault(a => a.IsFolder ? b.IsFolder
            : !b.IsFolder && a.ButtonLabel == b.Label && a.Actions.Length == b.Actions.Count
              && a.Actions.Zip(b.Actions, (x, y) => x.Type == y.Type).All(t => t));
}
