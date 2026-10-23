using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StreamDak;

/// <summary>Contenu de config.json (format <see cref="ConfigNormalizer.CurrentVersion"/>) : réglages globaux et profils.</summary>
public class DeckConfig
{
    /// <summary>Version du format. 1 (ou absente) : ancien format avec <c>Pages</c> à la racine ; 2 : <c>Profiles</c>.</summary>
    public int Version { get; set; } = 1;
    /// <summary>Index de l'écran (0 = premier). -1 = auto : premier écran secondaire s'il existe.</summary>
    public int Monitor { get; set; } = -1;
    /// <summary>Identifiant Windows de l'écran choisi (\\.\DISPLAY2) : prioritaire sur <see cref="Monitor"/>, dont l'index change quand on rebranche un écran.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MonitorDevice { get; set; }
    /// <summary>true = plein écran sur l'écran cible ; false = fenêtre (utile pour tester).</summary>
    public bool Fullscreen { get; set; } = true;
    /// <summary>true = reste au-dessus des autres fenêtres. Par défaut non.</summary>
    public bool AlwaysOnTop { get; set; }
    /// <summary>Colonnes / lignes de la grille. 0 = automatique selon la résolution (≈ 20 cases en FHD).</summary>
    public int Columns { get; set; }
    /// <summary>Lignes de la grille ; 0 = automatique selon la résolution.</summary>
    public int Rows { get; set; }
    /// <summary>Espacement entre les touches, en unités indépendantes de la résolution (DIP).</summary>
    public double Spacing { get; set; } = 14;
    /// <summary>Fond du deck ; la valeur par défaut (<see cref="ConfigNormalizer.DefaultBackground"/>) affiche le fond nuancé du thème.</summary>
    public string Background { get; set; } = "#0E1015";
    /// <summary>Thème : "system" (suit Windows), "dark", "light" ou "amoled" (sombre à fond noir).</summary>
    public string Theme { get; set; } = "system";
    /// <summary>"fluent" : les emojis connus sont affichés avec l'icône Fluent équivalente ; "emoji" : tels quels.</summary>
    public string IconStyle { get; set; } = "fluent";
    /// <summary>Lance StreamDak à l'ouverture de session Windows (clé Run de l'utilisateur).</summary>
    public bool StartWithWindows { get; set; }
    /// <summary>Supprime les animations (en plus du réglage Windows « Effets d'animation »).</summary>
    public bool ReduceMotion { get; set; }
    /// <summary>Nom du profil affiché sur le deck.</summary>
    public string? ActiveProfile { get; set; }
    /// <summary>Identifiants du catalogue d'actions mis en favoris.</summary>
    public List<string> Favorites { get; set; } = new();
    /// <summary>Identifiants du catalogue d'actions récemment ajoutés (le plus récent en premier).</summary>
    public List<string> Recents { get; set; } = new();
    /// <summary>Profils, dans l'ordre de l'éditeur ; au moins un après normalisation.</summary>
    public List<DeckProfile> Profiles { get; set; } = new();
    /// <summary>
    /// Réglages des intégrations (OBS, vMix…) par identifiant. Absent tant qu'aucune n'est configurée. Les secrets (mots de
    /// passe, jetons) n'y figurent jamais : ils sont dans le magasin chiffré de l'utilisateur.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, IntegrationConfig>? Integrations { get; set; }

    /// <summary>Ancien format (version 1). Lu pour migration vers <see cref="Profiles"/>, jamais réécrit.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DeckPage>? Pages { get; set; }
}

/// <summary>Réglages non secrets d'une intégration.</summary>
public class IntegrationConfig
{
    /// <summary>true : StreamDak se connecte au démarrage et se reconnecte si la connexion se perd.</summary>
    public bool Enabled { get; set; }
    /// <summary>Valeurs des champs de l'intégration (adresse, port…), par clé de champ.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Values { get; set; }
}

/// <summary>Un profil : ses pages, son format de grille et l'application qui l'affiche automatiquement.</summary>
public class DeckProfile
{
    /// <summary>Nom unique (sans distinction de casse), visé par les actions « profile ».</summary>
    public string Name { get; set; } = "Profil";
    /// <summary>Colonnes / lignes de ce profil (format de l'appareil). 0 = réglage global de la configuration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Columns { get; set; }
    /// <summary>Lignes de la grille de ce profil ; 0 = réglage global.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Rows { get; set; }
    /// <summary>Nom du programme (ex. « chrome ») : le profil s'affiche automatiquement quand cette application est au premier plan.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppMatch { get; set; }
    /// <summary>Pages du profil, au moins une après normalisation.</summary>
    public List<DeckPage> Pages { get; set; } = new();
}

/// <summary>Une page : une grille de boutons, affichée en onglet sauf si elle est masquée.</summary>
public class DeckPage
{
    /// <summary>Nom unique dans le profil, visé par les actions « page ».</summary>
    public string Name { get; set; } = "Page";
    /// <summary>Page non listée dans la barre d'onglets (accessible via une action "page").</summary>
    public bool Hidden { get; set; }
    /// <summary>Cases dans l'ordre de la grille. Une case <see cref="DeckButton.Blank"/> est un emplacement vide.</summary>
    public List<DeckButton> Buttons { get; set; } = new();

    /// <summary>Type de page : absent = grille de boutons ; <see cref="PageKinds.Web"/> = une page web qui remplit tout l'espace central.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }
    /// <summary>Adresse (http ou https) affichée par une page <see cref="PageKinds.Web"/>. Les boutons de la page sont conservés, masqués.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    /// <summary>Zoom d'une page web en pourcentage (25 à 500) ; 0 (absent) = 100 %.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Zoom { get; set; }

    /// <summary>true pour une page web (<see cref="Kind"/> = <see cref="PageKinds.Web"/>).</summary>
    [JsonIgnore]
    public bool IsWeb => string.Equals(Kind, PageKinds.Web, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Types de page (<see cref="DeckPage.Kind"/>).</summary>
public static class PageKinds
{
    /// <summary>Page web en plein cadre (WebView2 isolé, comme le widget web : ADR 0017 et 0018).</summary>
    public const string Web = "web";

    /// <summary>Type connu (en minuscules) ou null pour une grille de boutons (absent ou inconnu).</summary>
    public static string? Normalize(string? kind) => string.Equals(kind?.Trim(), Web, StringComparison.OrdinalIgnoreCase) ? Web : null;
}

/// <summary>Un bouton du deck : apparence, étapes, ou contenu s'il s'agit d'un dossier.</summary>
public class DeckButton
{
    /// <summary>Titre ; peut contenir des modèles dynamiques ({time}, {date}, {timer}).</summary>
    public string Label { get; set; } = "";
    /// <summary>Nom d'icône Fluent (« Play24 »), emoji ou court texte affiché au-dessus du libellé.</summary>
    public string Icon { get; set; } = "";
    /// <summary>Couleur du bouton (#RRGGBB) : pastille pleine et corps teinté de la touche.</summary>
    public string Color { get; set; } = "#334155";
    /// <summary>Étapes exécutées dans l'ordre ; vide pour une horloge ou un dossier.</summary>
    public List<DeckAction> Actions { get; set; } = new();

    /// <summary>Emplacement vide (conserve les positions des cases voisines).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Blank { get; set; }
    /// <summary>Masque le titre sur le bouton.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HideTitle { get; set; }
    /// <summary>Absent : titre sous l'icône. "top", "middle" ou "bottom" : titre superposé à cette hauteur.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TitlePosition { get; set; }
    /// <summary>Couleur du texte et de l'icône (défaut : blanc).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TextColor { get; set; }
    /// <summary>Nom d'une image importée (dossier Icons de la configuration) affichée à la place de l'icône.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImagePath { get; set; }
    /// <summary>"cover" (défaut, remplit le bouton) ou "contain" (image entière).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageFit { get; set; }
    /// <summary>Dossier : ce bouton ouvre une grille de sous-boutons au lieu de lancer des actions.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsFolder { get; set; }
    /// <summary>Contenu du dossier (dans l'ordre de la grille), si <see cref="IsFolder"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DeckButton>? Children { get; set; }
    /// <summary>Widget affiché à la place d'un bouton : « clock », « agenda » ou « web » (voir <see cref="WidgetTypes"/>). Absent pour un bouton ordinaire.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Widget { get; set; }
    /// <summary>Réglages du widget, par clé (voir <see cref="WidgetOptionKeys"/>) : format de l'heure, nombre d'événements, adresse de la page…</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? WidgetOptions { get; set; }
    /// <summary>Largeur d'un widget en cases (0 ou 1 : une case). Les cases recouvertes à droite doivent être vides.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ColSpan { get; set; }
    /// <summary>Hauteur d'un widget en cases (0 ou 1 : une case). Les cases recouvertes en dessous doivent être vides.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RowSpan { get; set; }
}

/// <summary>Une étape d'un bouton. <see cref="Type"/> est l'un des identifiants de <see cref="ActionTypes"/>.</summary>
public class DeckAction
{
    /// <summary>hotkey | text | media | launch | script | url | page | profile | delay | focus (Value = programme ou titre ; Args = programme à lancer si absent)</summary>
    public string Type { get; set; } = "";
    /// <summary>Paramètre principal (combinaison, programme, adresse, nom de page…) ; son sens dépend du type.</summary>
    public string Value { get; set; } = "";
    /// <summary>Paramètre secondaire facultatif (arguments, corps de requête, niveau de volume…).</summary>
    public string? Args { get; set; }
}
