using System;
using System.Collections.Generic;
using System.Linq;

namespace StreamDak;

/// <summary>Un modèle de profil : des boutons prêts à l'emploi, construits à partir du catalogue d'actions.</summary>
/// <param name="Id">Identifiant stable.</param>
/// <param name="Name">Nom proposé au profil créé.</param>
/// <param name="Description">À quoi sert le modèle et ce qu'il demande (intégration à activer…).</param>
/// <param name="Icon">Icône Fluent du menu.</param>
/// <param name="Columns">Colonnes de la grille du profil créé.</param>
/// <param name="Rows">Lignes de la grille du profil créé.</param>
/// <param name="Slots">Boutons dans l'ordre de la grille : identifiant du catalogue et, au besoin, argument de la première action.</param>
public sealed record ProfileTemplate(string Id, string Name, string Description, string Icon, int Columns, int Rows, IReadOnlyList<TemplateSlot> Slots)
{
    /// <summary>Construit le profil. Un identifiant absent du catalogue (intégration retirée) laisse une case vide plutôt que d'échouer.</summary>
    public DeckProfile Build()
    {
        var buttons = new List<DeckButton>();
        foreach (var slot in Slots)
        {
            var entry = slot.CatalogId == null ? null : ActionCatalog.Find(slot.CatalogId);
            if (entry == null) { buttons.Add(new DeckButton { Blank = true }); continue; }
            var button = entry.CreateButton();
            if (slot.Label != null) button.Label = slot.Label;
            if (slot.Arg != null && button.Actions.Count > 0) button.Actions[^1].Args = slot.Arg;
            if (slot.Value != null && button.Actions.Count > 0) button.Actions[^1].Value = slot.Value;
            buttons.Add(button);
        }
        while (buttons.Count < Columns * Rows) buttons.Add(new DeckButton { Blank = true });
        return new DeckProfile
        {
            Name = Name, Columns = Columns, Rows = Rows,
            Pages = new List<DeckPage> { new() { Name = "Accueil", Buttons = buttons } },
        };
    }
}

/// <summary>Une case d'un modèle.</summary>
/// <param name="CatalogId">Identifiant de l'entrée du catalogue, ou null pour une case vide.</param>
/// <param name="Arg">Argument de la dernière action (nom de scène, de source…).</param>
/// <param name="Label">Titre à la place de celui du catalogue.</param>
/// <param name="Value">Valeur de la dernière action à la place de celle du catalogue.</param>
public sealed record TemplateSlot(string? CatalogId, string? Arg = null, string? Label = null, string? Value = null);

/// <summary>Modèles de profils livrés : réunions, streaming OBS, montage vidéo, simulation de vol, poste de travail.</summary>
public static class ProfileTemplates
{
    static TemplateSlot S(string id, string? arg = null, string? label = null, string? value = null) => new(id, arg, label, value);
    static readonly TemplateSlot Empty = new(null);

    /// <summary>Tous les modèles.</summary>
    public static IReadOnlyList<ProfileTemplate> All { get; } = new[]
    {
        new ProfileTemplate("meetings", "Réunions", "Micro, caméra, main levée et raccrocher pour Zoom, Teams et Discord (raccourcis clavier, sans réglage).",
            "People24", 4, 3, new[]
            {
                S("preset.zoom.mute"), S("preset.zoom.video"), S("preset.zoom.hand"), S("preset.zoom.leave"),
                S("preset.teams.mute"), S("preset.teams.video"), S("preset.teams.hand"), S("preset.teams.leave"),
                S("preset.discord.mute"), S("preset.discord.deafen"), S("audio.mute"), S("media.playpause"),
            }),
        new ProfileTemplate("streaming", "Streaming OBS", "Scènes, micro, direct et enregistrement. Demande d'activer l'intégration OBS (Paramètres › Intégrations) et d'ajuster les noms de scènes.",
            "VideoClip24", 4, 3, new[]
            {
                S("obs.scene", "Jeu", "Jeu"), S("obs.scene", "Discussion", "Discussion"), S("obs.scene", "Pause", "Pause"), S("obs.mute", "Micro", "Micro"),
                S("obs.stream"), S("obs.record"), S("obs.virtualcam"), S("obs.replay"),
                S("sys.playpause"), S("sys.previous"), S("sys.next"), S("audio.mute"),
            }),
        new ProfileTemplate("editing", "Montage vidéo", "Outils de coupe de Premiere Pro et de DaVinci Resolve (raccourcis par défaut).",
            "Cut24", 4, 3, new[]
            {
                S("preset.premiere.play"), S("preset.premiere.razor"), S("preset.premiere.select"), S("preset.premiere.cut"),
                S("preset.davinci.play"), S("preset.davinci.blade"), S("preset.davinci.select"), S("preset.davinci.save"),
                S("preset.premiere.ripple"), S("sys.snip"), S("media.playpause"), S("audio.mute"),
            }),
        new ProfileTemplate("flight", "Simulation de vol", "Commandes d'X-Plane (à vérifier : les noms dépendent de l'avion). Demande d'activer l'intégration X-Plane.",
            "Airplane24", 4, 3, new[]
            {
                S("xplane.command", "sim/flight_controls/landing_gear_toggle", "Train"), S("xplane.command", "sim/flight_controls/flaps_up", "Volets -"),
                S("xplane.command", "sim/flight_controls/flaps_down", "Volets +"), S("xplane.command", "sim/autopilot/servos_toggle", "Pilote auto"),
                S("xplane.command", "sim/lights/landing_lights_toggle", "Phares"), S("xplane.command", "sim/lights/nav_lights_toggle", "Navigation"),
                S("xplane.command", "sim/lights/strobe_lights_toggle", "Strobes"), S("xplane.command", "sim/lights/beacon_lights_toggle", "Balise"),
                Empty, Empty, Empty, Empty,
            }),
        new ProfileTemplate("desktop", "Poste de travail", "Charge du PC, média en cours, volume et raccourcis Windows (intégration « Système Windows » pour les valeurs en direct).",
            "Desktop24", 4, 3, new[]
            {
                S("sys.playpause", null, "{sys.media.title:Média}"), S("sys.previous"), S("sys.next"), S("audio.mute"),
                S("audio.up"), S("audio.down"), S("sys.desktop"), S("sys.lock"),
                S("sys.snip"), S("sys.taskmgr", null, "CPU {sys.cpu} %"), S("sys.explorer"), S("sys.settings"),
            }),
    };

    /// <summary>Modèle d'après son identifiant, ou null.</summary>
    public static ProfileTemplate? Find(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}
