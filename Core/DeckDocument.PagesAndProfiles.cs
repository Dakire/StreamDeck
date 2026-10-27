using System;
using System.Collections.Generic;
using System.Linq;

namespace StreamDak;

/// <summary>Document : opérations annulables sur les pages et les profils (ajout, copie, renommage, déplacement, suppression).</summary>
public sealed partial class DeckDocument
{
    // ---------- pages ----------

    /// <summary>Ajoute une page (nom unique) à la fin du profil ; l'index de la page est dans le résultat.</summary>
    public OpResult AddPage(string profile, string? name = null)
    {
        return Edit("Ajouter une page", cfg =>
        {
            var p = FindProfile(profile);
            if (p == null) return OpResult.Fail("Profil introuvable");
            var taken = new HashSet<string>(p.Pages.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            p.Pages.Add(new DeckPage { Name = ConfigNormalizer.Unique(string.IsNullOrWhiteSpace(name) ? $"Page {p.Pages.Count + 1}" : name.Trim(), taken) });
            return OpResult.Success(p.Pages.Count - 1);
        });
    }

    /// <summary>Duplique une page, boutons compris, juste après l'originale.</summary>
    public OpResult DuplicatePage(string profile, int page)
    {
        return Edit("Dupliquer la page", _ =>
        {
            var p = FindProfile(profile); var src = FindPage(profile, page);
            if (p == null || src == null) return OpResult.Fail("Page introuvable");
            var copy = ConfigStore.Clone(src);
            var taken = new HashSet<string>(p.Pages.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            copy.Name = ConfigNormalizer.Unique(src.Name + " (copie)", taken);
            p.Pages.Insert(page + 1, copy);
            return OpResult.Success(page + 1);
        });
    }

    /// <summary>Renomme une page (nom unique, non vide) et met à jour les actions « page » qui la visent dans le profil.</summary>
    public OpResult RenamePage(string profile, int page, string newName)
    {
        newName = (newName ?? "").Trim();
        if (newName.Length == 0) return OpResult.Fail("Le nom ne peut pas être vide");
        return Edit("Renommer la page", _ =>
        {
            var p = FindProfile(profile); var pg = FindPage(profile, page);
            if (p == null || pg == null) return OpResult.Fail("Page introuvable");
            if (pg.Name.Equals(newName, StringComparison.Ordinal)) return OpResult.Fail("Nom inchangé");
            if (p.Pages.Where(x => !ReferenceEquals(x, pg)).Any(x => x.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                return OpResult.Fail($"Une page « {newName} » existe déjà");
            foreach (var a in AllActions(p.Pages.SelectMany(x => x.Buttons)))
                if (a.Type.Equals(ActionTypes.Page, StringComparison.OrdinalIgnoreCase) && a.Value.Equals(pg.Name, StringComparison.OrdinalIgnoreCase))
                    a.Value = newName;
            pg.Name = newName;
            return OpResult.Success(page);
        });
    }

    /// <summary>Masque ou affiche une page dans la barre d'onglets du deck (elle reste accessible par une action « page »).</summary>
    public OpResult SetPageHidden(string profile, int page, bool hidden) =>
        Edit(hidden ? "Masquer la page" : "Afficher la page", _ =>
        {
            var pg = FindPage(profile, page);
            if (pg == null) return OpResult.Fail("Page introuvable");
            if (pg.Hidden == hidden) return OpResult.Fail("Inchangé");
            pg.Hidden = hidden;
            return OpResult.Success(page);
        });

    /// <summary>
    /// Fait de la page une page web plein cadre (<paramref name="url"/> : texte saisi, complété en https://), ou la remet en grille de boutons
    /// (<paramref name="url"/> null). Les boutons de la page sont conservés dans les deux sens.
    /// </summary>
    public OpResult SetPageWeb(string profile, int page, string? url)
    {
        Uri? uri = null;
        if (url != null && !WebAddress.TryParse(url, out uri)) return OpResult.Fail("Adresse non valide : saisis une adresse web (https://…)");
        return Edit(uri == null ? "Page en grille de boutons" : "Page web", _ =>
        {
            var pg = FindPage(profile, page);
            if (pg == null) return OpResult.Fail("Page introuvable");
            string? kind = uri == null ? null : PageKinds.Web, address = uri?.AbsoluteUri;
            if (pg.Kind == kind && pg.Url == address) return OpResult.Fail("Inchangé");
            pg.Kind = kind;
            pg.Url = address;
            return OpResult.Success(page);
        });
    }

    /// <summary>Affiche ou masque le badge de notifications d'une page web (annulable).</summary>
    public OpResult SetPageBadgeEnabled(string profile, int page, bool enabled) =>
        Edit(enabled ? "Afficher le badge" : "Masquer le badge", _ =>
        {
            var pg = FindPage(profile, page);
            if (pg == null) return OpResult.Fail("Page introuvable");
            if (pg.BadgeEnabled == enabled) return OpResult.Fail("Inchangé");
            pg.BadgeEnabled = enabled;
            return OpResult.Success(page);
        });

    /// <summary>Règle l'interaction d'une page web avec la souris et le doigt (<see cref="WebInputModes"/>, annulable).</summary>
    public OpResult SetPageWebInput(string profile, int page, string mode) =>
        Edit("Interaction avec la page", _ =>
        {
            var pg = FindPage(profile, page);
            if (pg == null) return OpResult.Fail("Page introuvable");
            var stored = WebInputModes.ForStorage(mode);
            if (pg.WebInput == stored) return OpResult.Fail("Inchangé");
            pg.WebInput = stored;
            return OpResult.Success(page);
        });

    /// <summary>Exclut ou inclut le nombre d'une page web dans le total des notifications (annulable).</summary>
    public OpResult SetPageBadgeExcluded(string profile, int page, bool excluded) =>
        Edit(excluded ? "Exclure du total" : "Inclure dans le total", _ =>
        {
            var pg = FindPage(profile, page);
            if (pg == null) return OpResult.Fail("Page introuvable");
            if (pg.BadgeExcludeFromTotal == excluded) return OpResult.Fail("Inchangé");
            pg.BadgeExcludeFromTotal = excluded;
            return OpResult.Success(page);
        });

    /// <summary>Supprime une page ; la dernière page d'un profil est protégée.</summary>
    public OpResult DeletePage(string profile, int page) =>
        Edit("Supprimer la page", _ =>
        {
            var p = FindProfile(profile);
            if (p == null || FindPage(profile, page) == null) return OpResult.Fail("Page introuvable");
            if (p.Pages.Count <= 1) return OpResult.Fail("Un profil doit garder au moins une page");
            p.Pages.RemoveAt(page);
            return OpResult.Success(Math.Min(page, p.Pages.Count - 1));
        });

    /// <summary>Déplace une page de <paramref name="delta"/> rangs dans le profil.</summary>
    public OpResult MovePage(string profile, int page, int delta) =>
        Edit("Déplacer la page", _ =>
        {
            var p = FindProfile(profile);
            int to = page + delta;
            if (p == null || page < 0 || page >= p.Pages.Count || to < 0 || to >= p.Pages.Count) return OpResult.Fail("Déplacement impossible");
            var pg = p.Pages[page];
            p.Pages.RemoveAt(page);
            p.Pages.Insert(to, pg);
            return OpResult.Success(to);
        });

    // ---------- profils ----------

    /// <summary>Ajoute un profil vide (nom unique) avec une page ; format de grille facultatif (0 : réglage global).</summary>
    public OpResult AddProfile(string? name = null, int columns = 0, int rows = 0)
    {
        return Edit("Créer un profil", cfg =>
        {
            var taken = new HashSet<string>(cfg.Profiles.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            var profile = new DeckProfile
            {
                Name = ConfigNormalizer.Unique(string.IsNullOrWhiteSpace(name) ? "Nouveau profil" : name.Trim(), taken),
                Columns = columns, Rows = rows,
                Pages = new List<DeckPage> { new() { Name = "Page 1" } },
            };
            cfg.Profiles.Add(profile);
            return OpResult.Success(cfg.Profiles.Count - 1);
        });
    }

    /// <summary>Duplique un profil (nom unique) ; l'association à une application n'est pas copiée, elle doit rester unique.</summary>
    public OpResult DuplicateProfile(string name) =>
        Edit("Dupliquer le profil", cfg =>
        {
            var src = FindProfile(name);
            if (src == null) return OpResult.Fail("Profil introuvable");
            var copy = ConfigStore.Clone(src);
            var taken = new HashSet<string>(cfg.Profiles.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            copy.Name = ConfigNormalizer.Unique(src.Name + " (copie)", taken);
            copy.AppMatch = null;   // un même programme ne doit pas déclencher deux profils
            cfg.Profiles.Insert(cfg.Profiles.IndexOf(src) + 1, copy);
            return OpResult.Success(cfg.Profiles.IndexOf(copy));
        });

    /// <summary>Renomme un profil et met à jour le profil actif et les actions « profile » qui le visent.</summary>
    public OpResult RenameProfile(string name, string newName)
    {
        newName = (newName ?? "").Trim();
        if (newName.Length == 0) return OpResult.Fail("Le nom ne peut pas être vide");
        return Edit("Renommer le profil", cfg =>
        {
            var p = FindProfile(name);
            if (p == null) return OpResult.Fail("Profil introuvable");
            if (p.Name.Equals(newName, StringComparison.Ordinal)) return OpResult.Fail("Nom inchangé");
            if (cfg.Profiles.Where(x => !ReferenceEquals(x, p)).Any(x => x.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                return OpResult.Fail($"Un profil « {newName} » existe déjà");
            foreach (var a in AllActions(cfg.Profiles.SelectMany(x => x.Pages).SelectMany(x => x.Buttons)))
                if (a.Type.Equals(ActionTypes.Profile, StringComparison.OrdinalIgnoreCase) && a.Value.Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                    a.Value = newName;
            if (string.Equals(cfg.ActiveProfile, p.Name, StringComparison.OrdinalIgnoreCase)) cfg.ActiveProfile = newName;
            p.Name = newName;
            return OpResult.Success(cfg.Profiles.IndexOf(p));
        });
    }

    /// <summary>Supprime un profil ; le dernier est protégé et le profil actif passe au premier restant.</summary>
    public OpResult DeleteProfile(string name) =>
        Edit("Supprimer le profil", cfg =>
        {
            var p = FindProfile(name);
            if (p == null) return OpResult.Fail("Profil introuvable");
            if (cfg.Profiles.Count <= 1) return OpResult.Fail("Il faut garder au moins un profil");
            cfg.Profiles.Remove(p);
            if (string.Equals(cfg.ActiveProfile, p.Name, StringComparison.OrdinalIgnoreCase)) cfg.ActiveProfile = cfg.Profiles[0].Name;
            return OpResult.Success(0);
        });

    /// <summary>Fixe le format de grille du profil (colonnes × lignes, de 0 à 20 ; 0 : réglage global).</summary>
    public OpResult SetProfileLayout(string name, int columns, int rows) =>
        Edit("Format du profil", _ =>
        {
            var p = FindProfile(name);
            if (p == null) return OpResult.Fail("Profil introuvable");
            columns = Math.Clamp(columns, 0, 20); rows = Math.Clamp(rows, 0, 20);
            if (p.Columns == columns && p.Rows == rows) return OpResult.Fail("Inchangé");
            p.Columns = columns; p.Rows = rows;
            return OpResult.Success();
        });

    /// <summary>Associe le profil à un programme ; un même programme ne peut déclencher qu'un profil.</summary>
    public OpResult SetProfileApp(string name, string? app)
    {
        app = string.IsNullOrWhiteSpace(app) ? null : app.Trim();
        if (app != null && app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) app = app[..^4];
        return Edit("Application associée", cfg =>
        {
            var p = FindProfile(name);
            if (p == null) return OpResult.Fail("Profil introuvable");
            if (app != null && cfg.Profiles.Any(x => !ReferenceEquals(x, p) && string.Equals(x.AppMatch, app, StringComparison.OrdinalIgnoreCase)))
                return OpResult.Fail($"« {app} » est déjà associé à un autre profil");
            if (string.Equals(p.AppMatch, app, StringComparison.OrdinalIgnoreCase)) return OpResult.Fail("Inchangé");
            p.AppMatch = app;
            return OpResult.Success();
        });
    }

    /// <summary>Ajoute un profil importé (nom rendu unique).</summary>
    public OpResult AddImportedProfile(DeckProfile profile) =>
        Edit("Importer un profil", cfg =>
        {
            var taken = new HashSet<string>(cfg.Profiles.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            profile.Name = ConfigNormalizer.Unique(profile.Name, taken);
            if (cfg.Profiles.Any(p => string.Equals(p.AppMatch, profile.AppMatch, StringComparison.OrdinalIgnoreCase) && profile.AppMatch != null))
                profile.AppMatch = null;
            cfg.Profiles.Add(profile);
            return OpResult.Success(cfg.Profiles.Count - 1);
        });

    /// <summary>Profil affiché sur le deck (hors historique : c'est un réglage, pas un contenu).</summary>
    public bool SetActiveProfile(string name)
    {
        var p = FindProfile(name);
        if (p == null || p.Name.Equals(Config.ActiveProfile, StringComparison.Ordinal)) return false;
        UpdateSettings(c => c.ActiveProfile = p.Name, "Profil actif");
        return true;
    }
}
