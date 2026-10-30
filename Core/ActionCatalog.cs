using System;
using System.Collections.Generic;
using System.Linq;

namespace StreamDak;

/// <summary>Une entrée du catalogue : un bouton prêt à poser, avec ses actions pré-remplies.</summary>
public sealed record CatalogAction(
    string Id, string Category, string Name, string Description, string Icon, string Color,
    DeckAction[] Actions, string? Note = null, bool IsFolder = false, string Keywords = "", string? Label = null, string? Widget = null, string? WidgetUrl = null,
    IReadOnlyDictionary<string, string>? WidgetOptions = null, int Cols = 0, int Rows = 0)
{
    /// <summary>Titre du bouton créé (par défaut le nom ; « {time} » pour l'horloge).</summary>
    public string ButtonLabel => Label ?? Name;

    /// <summary>Bouton neuf (copie indépendante du catalogue).</summary>
    public DeckButton CreateButton()
    {
        if (IsFolder) return DeckDocument.NewFolder(Name);
        if (WidgetTypes.Find(Widget) is { } widget)
        {
            var button = new DeckButton { Label = ButtonLabel, Icon = Icon, Color = Color, Widget = widget.Id, ColSpan = widget.Cols, RowSpan = widget.Rows };
            if (WidgetUrl != null) button.SetOption(WidgetOptionKeys.Url, WidgetUrl);   // page prête à l'emploi : adresse déjà renseignée
            if (WidgetOptions != null) foreach (var (key, value) in WidgetOptions) button.SetOption(key, value);   // réglages prêts (canal Sonar, forme)
            if (Cols > 0) button.ColSpan = Cols;
            if (Rows > 0) button.RowSpan = Rows;
            return button;
        }
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
    /// <summary>Catégorie : raccourcis de Windows.</summary>
    public const string SystemCategory = "Système";
    /// <summary>Catégorie : lancement et mise au premier plan de programmes.</summary>
    public const string Apps = "Applications";
    /// <summary>Catégorie : touches multimédia.</summary>
    public const string Multimedia = "Multimédia";
    /// <summary>Catégorie : son et micro.</summary>
    public const string Audio = "Audio";
    /// <summary>Catégorie : combinaisons de touches courantes.</summary>
    public const string Keyboard = "Raccourcis clavier";
    /// <summary>Catégorie : sites web.</summary>
    public const string Web = "Sites web";
    /// <summary>Catégorie : scripts, enchaînements, requêtes web et minuteurs.</summary>
    public const string Automation = "Automatisation";
    /// <summary>Catégorie : changement de page et de profil.</summary>
    public const string Navigation = "Profils et navigation";
    /// <summary>Catégorie : widgets (horloge, agenda, page web), qui affichent une information au lieu de lancer une action.</summary>
    public const string Widgets = "Widgets";

    static readonly string[] BuiltInCategories = { Widgets, SystemCategory, Apps, AppShortcutPresets.Category, Multimedia, Audio, Keyboard, Web, Automation, Navigation };
    static readonly object Gate = new();
    static readonly List<string> ExtraCategories = new();
    static readonly Dictionary<string, CatalogAction> Extra = new();

    /// <summary>Catégories dans l'ordre d'affichage du catalogue : celles de StreamDak, puis une par intégration enregistrée.</summary>
    public static IReadOnlyList<string> Categories { get { lock (Gate) return BuiltInCategories.Concat(ExtraCategories).ToList(); } }

    /// <summary>Icône Fluent de chaque catégorie (titre de section du catalogue).</summary>
    public static readonly Dictionary<string, string> CategoryIcons = new(StringComparer.Ordinal)
    {
        [SystemCategory] = "Desktop24", [Apps] = "Apps24", [Multimedia] = "Play24", [Audio] = "Speaker224",
        [Widgets] = "Board24", [Keyboard] = "Keyboard24", [Web] = "Globe24", [Automation] = "Flash24", [Navigation] = "Navigation24", [AppShortcutPresets.Category] = AppShortcutPresets.CategoryIcon,
    };

    static DeckAction A(string type, string value, string? args = null) => new() { Type = type, Value = value, Args = args };

    // Ramène Chrome au premier plan (le lance s'il est fermé), laisse 150 ms à Windows, puis envoie le raccourci
    static DeckAction[] InChrome(string hotkey) => new[]
    {
        A(ActionTypes.Focus, "chrome", "chrome.exe"), A(ActionTypes.Delay, "150"), A(ActionTypes.Hotkey, hotkey),
    };

    static CatalogAction C(string id, string cat, string name, string desc, string icon, string color, DeckAction[] actions,
        string? note = null, string keywords = "") => new(id, cat, name, desc, icon, color, actions, note, false, keywords);

    /// <summary>Toutes les actions prêtes à l'emploi, dans l'ordre d'affichage : celles de StreamDak, puis celles des intégrations.</summary>
    public static IReadOnlyList<CatalogAction> All { get { lock (Gate) return BuiltIn.Concat(Extra.Values).ToList(); } }

    /// <summary>
    /// Ajoute les actions d'une intégration dans leur propre catégorie. Idempotent : un identifiant déjà enregistré est remplacé.
    /// Appelé au démarrage par la racine de composition.
    /// </summary>
    public static void Register(string category, string icon, IEnumerable<CatalogAction> actions)
    {
        lock (Gate)
        {
            if (!BuiltInCategories.Contains(category) && !ExtraCategories.Contains(category)) ExtraCategories.Add(category);
            CategoryIcons[category] = icon;
            foreach (var a in actions) Extra[a.Id] = a;
        }
    }

    static readonly CatalogAction[] CoreEntries = new[]
    {
        // --- Système ---
        C("sys.desktop", SystemCategory, "Bureau", "Affiche ou masque le bureau (Win+D).", "Desktop24", "#2563EB", new[] { A(ActionTypes.Hotkey, "win+d") }),
        C("sys.explorer", SystemCategory, "Explorateur", "Ouvre l'Explorateur de fichiers (Win+E).", "Folder24", "#334155", new[] { A(ActionTypes.Hotkey, "win+e") }, null, "fichiers"),
        C("sys.taskmgr", SystemCategory, "Gestionnaire des tâches", "Ouvre le Gestionnaire des tâches (Ctrl+Maj+Échap).", "DataBarVertical24", "#2563EB", new[] { A(ActionTypes.Hotkey, "ctrl+shift+esc") }, null, "processus"),
        C("sys.snip", SystemCategory, "Capture d'écran", "Outil de capture (Win+Maj+S).", "Cut24", "#2563EB", new[] { A(ActionTypes.Hotkey, "win+shift+s") }, null, "screenshot"),
        // Win+L simulé ne verrouille pas (Windows réserve cette combinaison au clavier physique) : LockWorkStation, l'API documentée
        C("sys.lock", SystemCategory, "Verrouiller la session", "Verrouille Windows (comme Win+L).", "LockClosed24", "#BE123C",
            new[] { A(ActionTypes.Launch, "rundll32.exe", "user32.dll,LockWorkStation") }, null, "verrou écran"),
        C("sys.run", SystemCategory, "Exécuter", "Ouvre la boîte Exécuter (Win+R).", "Rocket24", "#334155", new[] { A(ActionTypes.Hotkey, "win+r") }),
        C("sys.settings", SystemCategory, "Paramètres Windows", "Ouvre les Paramètres (Win+I).", "Settings24", "#334155", new[] { A(ActionTypes.Hotkey, "win+i") }),
        C("sys.search", SystemCategory, "Recherche Windows", "Ouvre la recherche (Win+S).", "Search24", "#334155", new[] { A(ActionTypes.Hotkey, "win+s") }),
        C("sys.switch", SystemCategory, "Changer de fenêtre", "Alt+Tab.", "Window24", "#334155", new[] { A(ActionTypes.Hotkey, "alt+tab") }, null, "alt tab"),
        // Outils d'administration de Windows (consoles .msc et panneaux .cpl, ouverts par Windows lui-même)
        C("sys.sound", SystemCategory, "Son : périphériques", "Ouvre le panneau Son : choisir la sortie et le micro par défaut, leurs niveaux.", "Speaker224", "#C2410C",
            new[] { A(ActionTypes.Launch, "mmsys.cpl") }, "Windows n'offre pas d'API publique pour changer le périphérique par défaut : ce panneau est la voie prise en charge.", "audio casque micro défaut"),
        C("sys.devmgmt", SystemCategory, "Gestionnaire de périphériques", "Pilotes et matériel.", "DeveloperBoard24", "#475569", new[] { A(ActionTypes.Launch, "devmgmt.msc") }, null, "pilotes matériel"),
        C("sys.services", SystemCategory, "Services", "Démarrer, arrêter, configurer les services Windows.", "Settings24", "#475569", new[] { A(ActionTypes.Launch, "services.msc") }, null, "admin"),
        C("sys.events", SystemCategory, "Observateur d'événements", "Journaux de Windows et des applications.", "DocumentText24", "#475569", new[] { A(ActionTypes.Launch, "eventvwr.msc") }, null, "journal logs admin"),
        C("sys.compmgmt", SystemCategory, "Gestion de l'ordinateur", "Disques, utilisateurs, services, journaux.", "Desktop24", "#475569", new[] { A(ActionTypes.Launch, "compmgmt.msc") }, null, "admin"),
        C("sys.disks", SystemCategory, "Gestion des disques", "Partitions et volumes.", "Storage24", "#475569", new[] { A(ActionTypes.Launch, "diskmgmt.msc") }, null, "partition volume"),
        C("sys.network", SystemCategory, "Connexions réseau", "Cartes réseau et leurs réglages.", "Router24", "#475569", new[] { A(ActionTypes.Launch, "ncpa.cpl") }, null, "ethernet wifi ip"),
        C("sys.tasks", SystemCategory, "Planificateur de tâches", "Tâches planifiées de Windows.", "CalendarClock24", "#475569", new[] { A(ActionTypes.Launch, "taskschd.msc") }, null, "admin"),
        C("sys.resmon", SystemCategory, "Moniteur de ressources", "Processeur, mémoire, disque et réseau par processus.", "DataUsage24", "#475569", new[] { A(ActionTypes.Launch, "resmon.exe") }, null, "performances"),
        C("sys.msinfo", SystemCategory, "Informations système", "Matériel, pilotes et version de Windows.", "Info24", "#475569", new[] { A(ActionTypes.Launch, "msinfo32.exe") }, null, "matériel version"),
        new("sys.clock", SystemCategory, "Horloge", "Affiche l'heure en direct.", "Clock24", "#334155", Array.Empty<DeckAction>(),
            "Le titre « {time} » affiche l'heure ; « {time:HH:mm:ss} » ajoute les secondes. Ces modèles marchent dans n'importe quel titre.",
            false, "heure temps", "{time}"),
        new("sys.date", SystemCategory, "Date", "Affiche la date du jour.", "Calendar24", "#334155", Array.Empty<DeckAction>(),
            "Le titre « {date:dddd d MMMM} » affiche par exemple « lundi 19 octobre » (formats de date .NET).", false, "jour calendrier", "{date:dddd d MMMM}"),

        // --- Applications ---
        C("app.launch", Apps, "Ouvrir un programme", "Lance un programme ou un dossier de ton choix.", "Rocket24", "#7C3AED", new[] { A(ActionTypes.Launch, "") }),
        C("app.focus", Apps, "Fenêtre au premier plan", "Ramène une fenêtre au premier plan (et lance le programme si besoin).", "Window24", "#7C3AED", new[] { A(ActionTypes.Focus, "") }),
        C("app.notepad", Apps, "Bloc-notes", "Lance le Bloc-notes.", "Notepad24", "#334155", new[] { A(ActionTypes.Launch, "notepad.exe") }),
        C("app.calc", Apps, "Calculatrice", "Lance la Calculatrice.", "Calculator24", "#334155", new[] { A(ActionTypes.Launch, "calc.exe") }),
        C("chrome.focus", Apps, "Chrome", "Ramène Chrome au premier plan, ou le lance s'il est fermé.", "Globe24", "#2563EB",
            new[] { A(ActionTypes.Focus, "chrome", "chrome.exe") }, "Ramène Chrome au premier plan, ou le lance s'il est fermé.", "navigateur google"),
        C("chrome.newtab", Apps, "Chrome : nouvel onglet", "Ctrl+T dans Chrome.", "Add24", "#2563EB", InChrome("ctrl+t")),
        C("chrome.reopen", Apps, "Chrome : rouvrir l'onglet", "Ctrl+Maj+T dans Chrome.", "ArrowUndo24", "#2563EB", InChrome("ctrl+shift+t")),
        C("chrome.close", Apps, "Chrome : fermer l'onglet", "Ctrl+W dans Chrome.", "Dismiss24", "#BE123C", InChrome("ctrl+w")),
        C("chrome.next", Apps, "Chrome : onglet suivant", "Ctrl+Tab dans Chrome.", "ArrowRight24", "#334155", InChrome("ctrl+tab")),
        C("chrome.prev", Apps, "Chrome : onglet précédent", "Ctrl+Maj+Tab dans Chrome.", "ArrowLeft24", "#334155", InChrome("ctrl+shift+tab")),
        C("chrome.reload", Apps, "Chrome : actualiser", "Ctrl+R dans Chrome.", "ArrowSync24", "#334155", InChrome("ctrl+r")),
        C("chrome.private", Apps, "Chrome : navigation privée", "Ctrl+Maj+N dans Chrome.", "Eye24", "#7C3AED", InChrome("ctrl+shift+n")),
        C("3cx.focus", Apps, "3CX", "Ramène 3CX au premier plan.", "Call24", "#047857", new[] { A(ActionTypes.Focus, "3CX", "") },
            "Recherche « 3CX » dans le titre de la fenêtre. Pour le lancer s'il est fermé, indique son chemin dans « Si absente, lancer ».", "téléphone"),
        C("3cx.call", Apps, "3CX : appeler", "Compose un numéro (lien tel:).", "Call24", "#047857", new[] { A(ActionTypes.Url, "tel:+33") },
            "Remplace +33 par le numéro à appeler. Fonctionne si 3CX est l'application d'appel par défaut de Windows (Paramètres › Applications par défaut › TEL)."),
        C("3cx.hotkey", Apps, "3CX : raccourci", "Ramène 3CX au premier plan puis envoie un raccourci.", "Headphones24", "#047857",
            new[] { A(ActionTypes.Focus, "3CX", ""), A(ActionTypes.Delay, "150"), A(ActionTypes.Hotkey, "") },
            "Saisis dans « Combinaison » le raccourci de ta version de 3CX (décrocher, raccrocher, couper le micro…)."),
        C("keeper.vault", Apps, "Keeper : coffre web", "Ouvre le coffre Keeper dans le navigateur.", "LockClosed24", "#7C3AED",
            new[] { A(ActionTypes.Url, "https://keepersecurity.com/vault") },
            "Si ton compte est en Europe, remplace keepersecurity.com par keepersecurity.eu.", "mot de passe"),
        C("keeper.fill", Apps, "Keeper : remplir", "Envoie le raccourci de l'extension Keeper à Chrome.", "Key24", "#7C3AED",
            new[] { A(ActionTypes.Focus, "chrome", "chrome.exe"), A(ActionTypes.Delay, "150"), A(ActionTypes.Hotkey, "") },
            "Définis d'abord le raccourci de l'extension Keeper dans chrome://extensions/shortcuts, puis saisis-le dans « Combinaison ». Ne stocke jamais de mot de passe dans un bouton : il serait écrit en clair dans la configuration."),
        C("keeper.app", Apps, "Keeper : application", "Ramène l'application Keeper au premier plan.", "Key24", "#7C3AED", new[] { A(ActionTypes.Focus, "Keeper", "") }),

        // --- Multimédia ---
        C("media.playpause", Multimedia, "Lecture / Pause", "Touche média Lecture/Pause.", "Play24", "#047857", new[] { A(ActionTypes.Media, "playpause") }),
        C("media.prev", Multimedia, "Précédent", "Piste précédente.", "Previous24", "#334155", new[] { A(ActionTypes.Media, "prev") }),
        C("media.next", Multimedia, "Suivant", "Piste suivante.", "Next24", "#334155", new[] { A(ActionTypes.Media, "next") }),
        C("media.stop", Multimedia, "Stop", "Arrête la lecture.", "Stop24", "#334155", new[] { A(ActionTypes.Media, "stop") }),

        // --- Audio ---
        C("audio.mute", Audio, "Muet", "Coupe ou rétablit le son.", "SpeakerMute24", "#BE123C", new[] { A(ActionTypes.Media, "mute") }),
        C("audio.down", Audio, "Volume -", "Baisse le volume.", "Speaker124", "#334155", new[] { A(ActionTypes.Media, "voldown") }),
        C("audio.up", Audio, "Volume +", "Augmente le volume.", "Speaker224", "#334155", new[] { A(ActionTypes.Media, "volup") }),
        C("audio.mic", Audio, "Micro", "Coupe ou rétablit le micro par défaut.", "Mic24", "#BE123C", new[] { A(ActionTypes.Audio, "mic-toggle") },
            "Agit sur le micro de communication par défaut de Windows ; l'état s'affiche dans la barre du deck.", "microphone muet visio"),
        C("audio.level", Audio, "Volume 50 %", "Règle le volume de sortie à un niveau précis.", "Speaker224", "#334155", new[] { A(ActionTypes.Audio, "volume", "50") },
            "Change le niveau dans « Volume (0 à 100) ».", "niveau son"),

        C("audio.device.mute", Audio, "Micro ou sortie d'une carte son", "Coupe ou rétablit un périphérique audio choisi par son nom (micro Focusrite, casque Arctis…), même s'il n'est pas celui par défaut.",
            "Mic24", "#BE123C", new[] { A(ActionTypes.AudioDevice, "toggle", "capture:Focusrite") },
            "Remplace « Focusrite » par une partie du nom du périphérique (« capture: » pour un micro, « render: » pour une sortie). Le choix « Lister les périphériques » affiche les noms.", "focusrite scarlett arctis interface"),
        C("audio.device.level", Audio, "Volume d'un périphérique", "Règle le volume d'un périphérique audio choisi par son nom, sans changer le périphérique par défaut.",
            "Speaker224", "#334155", new[] { A(ActionTypes.AudioDevice, "volume", "render:Arctis=50") },
            "« nom=niveau » : 0 à 100, ou +5 / -5 pour ajuster.", "niveau casque arctis focusrite"),

        // --- Raccourcis clavier ---
        C("key.hotkey", Keyboard, "Raccourci clavier", "Envoie une combinaison de touches.", "Keyboard24", "#334155", new[] { A(ActionTypes.Hotkey, "") }),
        C("key.text", Keyboard, "Saisie de texte", "Tape un texte dans l'application active.", "TextT24", "#334155", new[] { A(ActionTypes.Text, "") }),
        C("key.copy", Keyboard, "Copier", "Ctrl+C.", "Copy24", "#334155", new[] { A(ActionTypes.Hotkey, "ctrl+c") }),
        C("key.paste", Keyboard, "Coller", "Ctrl+V.", "ClipboardPaste24", "#334155", new[] { A(ActionTypes.Hotkey, "ctrl+v") }),
        C("key.cut", Keyboard, "Couper", "Ctrl+X.", "Cut24", "#334155", new[] { A(ActionTypes.Hotkey, "ctrl+x") }),
        C("key.undo", Keyboard, "Annuler", "Ctrl+Z.", "ArrowUndo24", "#334155", new[] { A(ActionTypes.Hotkey, "ctrl+z") }),
        C("key.redo", Keyboard, "Rétablir", "Ctrl+Y.", "ArrowRedo24", "#334155", new[] { A(ActionTypes.Hotkey, "ctrl+y") }),
        C("key.save", Keyboard, "Enregistrer", "Ctrl+S.", "Save24", "#334155", new[] { A(ActionTypes.Hotkey, "ctrl+s") }),

        // --- Sites web ---
        C("web.url", Web, "Ouvrir un site web", "Ouvre l'adresse de ton choix dans le navigateur par défaut.", "Link24", "#2563EB", new[] { A(ActionTypes.Url, "https://") }),
        C("web.google", Web, "Google", "google.com", "Search24", "#2563EB", new[] { A(ActionTypes.Url, "https://www.google.com") }),
        C("web.gmail", Web, "Gmail", "mail.google.com", "Mail24", "#BE123C", new[] { A(ActionTypes.Url, "https://mail.google.com") }),
        C("web.drive", Web, "Drive", "drive.google.com", "Folder24", "#047857", new[] { A(ActionTypes.Url, "https://drive.google.com") }),
        C("web.calendar", Web, "Agenda", "calendar.google.com", "Calendar24", "#2563EB", new[] { A(ActionTypes.Url, "https://calendar.google.com") }),
        C("web.meet", Web, "Meet", "meet.google.com", "Video24", "#047857", new[] { A(ActionTypes.Url, "https://meet.google.com") }),
        C("web.docs", Web, "Docs", "docs.google.com", "Document24", "#2563EB", new[] { A(ActionTypes.Url, "https://docs.google.com") }),
        C("web.maps", Web, "Maps", "maps.google.com", "Map24", "#B45309", new[] { A(ActionTypes.Url, "https://maps.google.com") }),

        // --- Automatisation ---
        C("auto.script", Automation, "Script PowerShell", "Exécute une commande PowerShell ou un fichier .ps1 / .bat / .cmd.", "Flash24", "#7C3AED", new[] { A(ActionTypes.Script, "") }),
        C("auto.multi", Automation, "Multi-actions", "Enchaîne plusieurs étapes : programme, pause, raccourci, lien…", "Layer24", "#7C3AED",
            new[] { A(ActionTypes.Launch, ""), A(ActionTypes.Delay, "500"), A(ActionTypes.Hotkey, "") }, "Ajoute, réordonne et supprime les étapes dans le panneau de propriétés.", "macro séquence"),
        C("auto.delay", Automation, "Pause", "Attend avant l'étape suivante (à utiliser dans un enchaînement).", "Timer24", "#334155", new[] { A(ActionTypes.Delay, "500") }),
        C("auto.webhook", Automation, "Requête web", "Appelle une adresse (webhook, domotique, API) : GET, ou POST avec un corps.", "Send24", "#7C3AED",
            new[] { A(ActionTypes.Http, "https://") }, "Le corps (facultatif) est envoyé en JSON s'il commence par { ou [. Délai maximal : 10 secondes.", "webhook api http post domotique"),
        C("auto.timer", Automation, "Minuteur", "Compte à rebours affiché sur le bouton ; toucher à nouveau l'arrête.", "Timer24", "#B45309",
            new[] { A(ActionTypes.Timer, "300") }, "Durée en secondes (300 = 5 minutes). Un son et un message signalent la fin.", "compte à rebours"),
        C("auto.stopwatch", Automation, "Chronomètre", "Compte le temps écoulé sur le bouton ; toucher à nouveau l'arrête et affiche le temps mesuré.", "Timer24", "#0F766E",
            new[] { A(ActionTypes.Timer, "0") }, "Un minuteur de durée 0 est un chronomètre.", "chrono temps écoulé"),
        // Développeur et IT : le programme et ses arguments sont transmis tels quels (aucune ligne de commande assemblée), %VARIABLES% développées
        C("dev.terminal", Automation, "Terminal dans un dossier", "Ouvre Windows Terminal dans le dossier choisi.", "WindowConsole20", "#1F2937",
            new[] { A(ActionTypes.Launch, "wt.exe", "-d \"%USERPROFILE%\"") }, "Change le dossier après -d. Windows Terminal est fourni avec Windows 11.", "console powershell cmd"),
        C("dev.ssh", Automation, "Connexion SSH", "Ouvre une session SSH dans Windows Terminal.", "Server24", "#1F2937",
            new[] { A(ActionTypes.Launch, "wt.exe", "ssh utilisateur@serveur") }, "Remplace utilisateur@serveur (ou un hôte de ~/.ssh/config). Le mot de passe se tape dans le terminal : StreamDak ne l'enregistre pas.", "serveur shell linux"),
        C("dev.vscode", Automation, "Projet dans VS Code", "Ouvre un dossier de projet dans Visual Studio Code.", "Code24", "#2563EB",
            new[] { A(ActionTypes.Launch, "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe", "\"%USERPROFILE%\\Projets\"") },
            "Change le dossier du projet. Installation pour tous les utilisateurs : C:\\Program Files\\Microsoft VS Code\\Code.exe.", "éditeur code développement"),

        // --- Profils et navigation ---
        new("nav.folder", Navigation, "Dossier", "Regroupe des boutons dans une grille imbriquée.", "Folder24", "#B45309", Array.Empty<DeckAction>(), null, true, "sous-dossier"),
        C("nav.page", Navigation, "Aller à une page", "Affiche une autre page du profil.", "Document24", "#B45309", new[] { A(ActionTypes.Page, PageTargets.Home) }),
        C("nav.home", Navigation, "Accueil", "Retourne à la première page.", "Home24", "#B45309", new[] { A(ActionTypes.Page, PageTargets.Home) }),
        C("nav.back", Navigation, "Retour", "Retourne à la page précédente.", "ArrowLeft24", "#B45309", new[] { A(ActionTypes.Page, PageTargets.Back) }),
        C("nav.profile", Navigation, "Changer de profil", "Affiche un autre profil sur le deck.", "PersonSwap24", "#7C3AED", new[] { A(ActionTypes.Profile, "") }),
    };

    static readonly CatalogAction[] WidgetEntries = WidgetTypes.All.Select(w => new CatalogAction("widget." + w.Id, Widgets, w.Name, w.Description, w.Icon, w.Color,
        Array.Empty<DeckAction>(), "Un widget occupe plusieurs cases : les cases voisines doivent être vides. Taille réglable dans les propriétés.",
        false, "widget affichage information", null, w.Id)).ToArray();

    // Pages Google prêtes à l'emploi : le widget web avec l'adresse déjà renseignée (la connexion à Google se fait une fois dans le cadre)
    static CatalogAction GooglePage(string id, string name, string description, string icon, string color, string url) =>
        new("widget." + id, Widgets, name, description, icon, color, Array.Empty<DeckAction>(),
            "S'affiche dans un cadre isolé : connecte-toi à Google une fois dans ce cadre (touche « Saisie au clavier » pour taper). Taille réglable.",
            false, "google widget web iframe " + id, null, WidgetTypes.Web, url);

    static readonly CatalogAction[] GoogleEntries =
    {
        GooglePage("gchat", "Google Chat", "Google Chat dans un cadre du deck.", "Chat24", "#188038", "https://chat.google.com/"),
        GooglePage("gcalendar", "Google Agenda (page web)", "L'agenda Google complet, en semaine.", "CalendarLtr24", "#1A73E8", "https://calendar.google.com/calendar/u/0/r/week"),
        GooglePage("gmail", "Gmail", "Ta boîte de réception Gmail.", "Mail24", "#C5221F", "https://mail.google.com/mail/u/0/#inbox"),
    };

    static readonly IReadOnlyList<CatalogAction> BuiltIn = WidgetEntries.Concat(GoogleEntries).Concat(CoreEntries).Concat(AppShortcutPresets.Entries).ToList();

    static readonly Dictionary<string, CatalogAction> ById = BuiltIn.ToDictionary(a => a.Id);

    /// <summary>Action du catalogue d'après son identifiant stable (« sys.desktop »), ou null : les favoris et récents d'une ancienne version peuvent ne plus exister.</summary>
    public static CatalogAction? Find(string? id)
    {
        if (id == null) return null;
        if (ById.TryGetValue(id, out var a)) return a;
        lock (Gate) return Extra.GetValueOrDefault(id);
    }

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

    /// <summary>Texte sans accents ni majuscules, pour une recherche tolérante (« reseau » trouve « Réseau »).</summary>
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
