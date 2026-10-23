using System.Collections.Generic;

namespace StreamDak;

/// <summary>
/// Raccourcis prêts à l'emploi pour des applications sans API locale (réunions, création, bureautique, jeu) : StreamDak ramène la
/// fenêtre de l'application au premier plan, attend 150 ms puis envoie son raccourci. Aucun état n'est lisible dans ce mode.
/// Les raccourcis sont ceux par défaut de chaque application ; s'ils ont été changés, la combinaison se modifie dans l'éditeur.
/// </summary>
public static class AppShortcutPresets
{
    /// <summary>Catégorie du catalogue.</summary>
    public const string Category = "Applications cibles";
    /// <summary>Icône de la catégorie.</summary>
    public const string CategoryIcon = "Keyboard24";

    const string Note = "Raccourci par défaut de l'application. S'il a été modifié chez toi, change la combinaison dans l'éditeur.";
    const string BlenderNote = "Les raccourcis de Blender agissent sur la zone sous la souris : garde le pointeur au-dessus de la vue 3D.";

    static CatalogAction P(string id, string app, string name, string desc, string icon, string color, string window, string hotkey,
        string? note = null, string keywords = "")
    {
        var actions = new[]
        {
            new DeckAction { Type = ActionTypes.Focus, Value = window, Args = "" },
            new DeckAction { Type = ActionTypes.Delay, Value = "150" },
            new DeckAction { Type = ActionTypes.Hotkey, Value = hotkey },
        };
        return new CatalogAction(id, Category, $"{app} : {name}", desc, icon, color, actions, note ?? Note, false, $"{app} {keywords}", $"{app} : {name}");
    }

    // Raccourci global de l'application (Discord) : pas de mise au premier plan, la fenêtre peut rester cachée
    static CatalogAction G(string id, string app, string name, string desc, string icon, string color, string hotkey, string note, string keywords = "") =>
        new(id, Category, $"{app} : {name}", desc, icon, color, new[] { new DeckAction { Type = ActionTypes.Hotkey, Value = hotkey } }, note, false, $"{app} {keywords}", $"{app} : {name}");

    /// <summary>Toutes les entrées, dans l'ordre d'affichage.</summary>
    public static IReadOnlyList<CatalogAction> Entries { get; } = new[]
    {
        // --- Zoom (raccourcis par défaut de la fenêtre de réunion) ---
        P("preset.zoom.mute", "Zoom", "micro", "Coupe ou rétablit le micro (Alt+A).", "MicOff24", "#2563EB", "Zoom", "alt+a", null, "réunion visio"),
        P("preset.zoom.video", "Zoom", "caméra", "Démarre ou arrête la vidéo (Alt+V).", "Video24", "#2563EB", "Zoom", "alt+v", null, "réunion visio webcam"),
        P("preset.zoom.hand", "Zoom", "lever la main", "Lève ou baisse la main (Alt+Y).", "HandLeft24", "#2563EB", "Zoom", "alt+y", null, "réunion visio"),
        P("preset.zoom.share", "Zoom", "partager l'écran", "Démarre ou arrête le partage d'écran (Alt+Maj+S).", "ShareScreenStart24", "#2563EB", "Zoom", "alt+shift+s", null, "réunion visio"),
        P("preset.zoom.leave", "Zoom", "quitter", "Quitte la réunion (Alt+Q).", "CallEnd24", "#BE123C", "Zoom", "alt+q", null, "réunion visio raccrocher"),

        // --- Microsoft Teams (nouvelle version) ---
        P("preset.teams.mute", "Teams", "micro", "Coupe ou rétablit le micro (Ctrl+Maj+M).", "MicOff24", "#6264A7", "Teams", "ctrl+shift+m", null, "réunion visio"),
        P("preset.teams.video", "Teams", "caméra", "Démarre ou arrête la caméra (Ctrl+Maj+O).", "Video24", "#6264A7", "Teams", "ctrl+shift+o", null, "réunion visio webcam"),
        P("preset.teams.hand", "Teams", "lever la main", "Lève ou baisse la main (Ctrl+Maj+K).", "HandLeft24", "#6264A7", "Teams", "ctrl+shift+k", null, "réunion visio"),
        P("preset.teams.share", "Teams", "partager", "Ouvre le partage de contenu (Ctrl+Maj+E).", "ShareScreenStart24", "#6264A7", "Teams", "ctrl+shift+e", null, "réunion visio écran"),
        P("preset.teams.leave", "Teams", "raccrocher", "Quitte l'appel ou la réunion (Ctrl+Maj+H).", "CallEnd24", "#BE123C", "Teams", "ctrl+shift+h", null, "réunion visio quitter"),

        // --- Discord (raccourcis globaux : à définir dans Paramètres › Raccourcis clavier) ---
        G("preset.discord.mute", "Discord", "micro", "Coupe ou rétablit le micro (Ctrl+Maj+M).", "MicOff24", "#4752C4", "ctrl+shift+m",
            "Raccourci global à définir dans Discord : Paramètres › Raccourcis clavier › Activer/désactiver le micro. Il fonctionne même quand Discord est réduit.", "vocal"),
        G("preset.discord.deafen", "Discord", "sourdine", "Coupe ou rétablit le casque (Ctrl+Maj+D).", "Headphones24", "#4752C4", "ctrl+shift+d",
            "Raccourci global à définir dans Discord : Paramètres › Raccourcis clavier › Activer/désactiver la sourdine.", "vocal casque"),

        // --- Adobe ---
        P("preset.premiere.play", "Premiere", "lecture", "Lecture ou pause du moniteur (Espace).", "Play24", "#5B4FC9", "Premiere Pro", "space", null, "adobe montage vidéo"),
        P("preset.premiere.razor", "Premiere", "lame", "Outil Lame (C).", "Cut24", "#5B4FC9", "Premiere Pro", "c", null, "adobe montage couper"),
        P("preset.premiere.select", "Premiere", "sélection", "Outil Sélection (V).", "CursorHover24", "#5B4FC9", "Premiere Pro", "v", null, "adobe montage"),
        P("preset.premiere.cut", "Premiere", "couper ici", "Coupe le clip à la tête de lecture (Ctrl+K).", "Cut24", "#5B4FC9", "Premiere Pro", "ctrl+k", null, "adobe montage couper"),
        P("preset.premiere.ripple", "Premiere", "supprimer et combler", "Supprime la sélection et comble le vide (Maj+Suppr).", "Delete24", "#BE123C", "Premiere Pro", "shift+delete", null, "adobe montage ripple"),
        P("preset.photoshop.brush", "Photoshop", "pinceau", "Outil Pinceau (B).", "PaintBrush24", "#0B6BB8", "Photoshop", "b", null, "adobe retouche"),
        P("preset.photoshop.eraser", "Photoshop", "gomme", "Outil Gomme (E).", "Eraser24", "#0B6BB8", "Photoshop", "e", null, "adobe retouche"),
        P("preset.photoshop.move", "Photoshop", "déplacement", "Outil Déplacement (V).", "ArrowMove24", "#0B6BB8", "Photoshop", "v", null, "adobe retouche"),
        P("preset.photoshop.stamp", "Photoshop", "tamponner le visible", "Fusionne les calques visibles dans un nouveau calque (Ctrl+Alt+Maj+E).", "Layer24", "#0B6BB8", "Photoshop", "ctrl+alt+shift+e", null, "adobe retouche calque"),

        // --- DaVinci Resolve ---
        P("preset.davinci.play", "DaVinci", "lecture", "Lecture ou pause (Espace).", "Play24", "#C2410C", "DaVinci Resolve", "space", null, "resolve montage vidéo étalonnage"),
        P("preset.davinci.blade", "DaVinci", "lame", "Outil Lame (B).", "Cut24", "#C2410C", "DaVinci Resolve", "b", null, "resolve montage couper"),
        P("preset.davinci.select", "DaVinci", "sélection", "Outil Sélection (A).", "CursorHover24", "#C2410C", "DaVinci Resolve", "a", null, "resolve montage"),
        P("preset.davinci.save", "DaVinci", "enregistrer", "Enregistre le projet (Ctrl+S).", "Save24", "#C2410C", "DaVinci Resolve", "ctrl+s", null, "resolve projet"),

        // --- Blender (la souris doit rester au-dessus de la vue 3D) ---
        P("preset.blender.mode", "Blender", "mode objet / édition", "Bascule entre le mode objet et le mode édition (Tab).", "Cube24", "#B45309", "Blender", "tab", BlenderNote, "3d modélisation"),
        P("preset.blender.grab", "Blender", "déplacer", "Déplace la sélection (G).", "ArrowMove24", "#B45309", "Blender", "g", BlenderNote, "3d modélisation"),
        P("preset.blender.render", "Blender", "rendu", "Lance le rendu de l'image (F12).", "Image24", "#B45309", "Blender", "f12", null, "3d"),
        P("preset.blender.undo", "Blender", "annuler", "Annule la dernière opération (Ctrl+Z).", "ArrowUndo24", "#B45309", "Blender", "ctrl+z", null, "3d"),

        // --- Office ---
        P("preset.outlook.new", "Outlook", "nouveau message", "Écrit un nouveau message (Ctrl+Maj+M).", "Mail24", "#0F6CBD", "Outlook", "ctrl+shift+m", null, "mail courriel office"),
        P("preset.outlook.reply", "Outlook", "répondre", "Répond au message sélectionné (Ctrl+R).", "ArrowReply24", "#0F6CBD", "Outlook", "ctrl+r", null, "mail courriel office"),
        P("preset.outlook.replyall", "Outlook", "répondre à tous", "Répond à tous (Ctrl+Maj+R).", "ArrowReplyAll24", "#0F6CBD", "Outlook", "ctrl+shift+r", null, "mail courriel office"),
        P("preset.word.save", "Word", "enregistrer", "Enregistre le document (Ctrl+S).", "Save24", "#185ABD", "Word", "ctrl+s", null, "office document"),
        P("preset.excel.save", "Excel", "enregistrer", "Enregistre le classeur (Ctrl+S).", "Save24", "#107C41", "Excel", "ctrl+s", null, "office tableur"),

        // --- Star Citizen : pas de raccourcis fixes, tout dépend des assignations de l'utilisateur ---
        P("preset.starcitizen.key", "Star Citizen", "touche", "Envoie une touche à Star Citizen.", "Games24", "#0369A1", "Star Citizen", "",
            "Saisis dans « Combinaison » la touche assignée dans le jeu. Certains anti-triche ignorent les touches simulées : à essayer en fenêtré.", "jeu vaisseau"),
    };
}
