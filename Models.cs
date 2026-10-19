using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TouchDeck;

public class DeckConfig
{
    /// <summary>Version du format. 1 (ou absente) : ancien format avec <c>Pages</c> à la racine ; 2 : <c>Profiles</c>.</summary>
    public int Version { get; set; } = 1;
    /// <summary>Index de l'écran (0 = premier). -1 = auto : premier écran secondaire s'il existe.</summary>
    public int Monitor { get; set; } = -1;
    /// <summary>true = plein écran sur l'écran cible ; false = fenêtre (utile pour tester).</summary>
    public bool Fullscreen { get; set; } = true;
    /// <summary>true = reste au-dessus des autres fenêtres. Par défaut non.</summary>
    public bool AlwaysOnTop { get; set; } = false;
    /// <summary>Colonnes / lignes de la grille. 0 = automatique selon la résolution (≈ 20 cases en FHD).</summary>
    public int Columns { get; set; } = 0;
    public int Rows { get; set; } = 0;
    public double Spacing { get; set; } = 14;
    public string Background { get; set; } = "#0E1015";
    /// <summary>Thème : "system" (suit Windows), "dark" ou "light".</summary>
    public string Theme { get; set; } = "system";
    /// <summary>"fluent" : les emojis connus sont affichés avec l'icône Fluent équivalente ; "emoji" : tels quels.</summary>
    public string IconStyle { get; set; } = "fluent";
    /// <summary>Lance TouchDeck à l'ouverture de session Windows (clé Run de l'utilisateur).</summary>
    public bool StartWithWindows { get; set; }
    /// <summary>Supprime les animations (en plus du réglage Windows « Effets d'animation »).</summary>
    public bool ReduceMotion { get; set; }
    /// <summary>Nom du profil affiché sur le deck.</summary>
    public string? ActiveProfile { get; set; }
    /// <summary>Identifiants du catalogue d'actions mis en favoris.</summary>
    public List<string> Favorites { get; set; } = new();
    /// <summary>Identifiants du catalogue d'actions récemment ajoutés (le plus récent en premier).</summary>
    public List<string> Recents { get; set; } = new();
    public List<DeckProfile> Profiles { get; set; } = new();

    /// <summary>Ancien format (version 1). Lu pour migration vers <see cref="Profiles"/>, jamais réécrit.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DeckPage>? Pages { get; set; }
}

public class DeckProfile
{
    public string Name { get; set; } = "Profil";
    /// <summary>Colonnes / lignes de ce profil (format de l'appareil). 0 = réglage global de la configuration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Columns { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Rows { get; set; }
    /// <summary>Nom du programme (ex. « chrome ») : le profil s'affiche automatiquement quand cette application est au premier plan.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppMatch { get; set; }
    public List<DeckPage> Pages { get; set; } = new();
}

public class DeckPage
{
    public string Name { get; set; } = "Page";
    /// <summary>Page non listée dans la barre d'onglets (accessible via une action "page").</summary>
    public bool Hidden { get; set; }
    /// <summary>Cases dans l'ordre de la grille. Une case <see cref="DeckButton.Blank"/> est un emplacement vide.</summary>
    public List<DeckButton> Buttons { get; set; } = new();
}

public class DeckButton
{
    public string Label { get; set; } = "";
    /// <summary>Nom d'icône Fluent (« Play24 »), emoji ou court texte affiché au-dessus du libellé.</summary>
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "#334155";
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DeckButton>? Children { get; set; }
}

public class DeckAction
{
    /// <summary>hotkey | text | media | launch | script | url | page | profile | delay | focus (Value = programme ou titre ; Args = programme à lancer si absent)</summary>
    public string Type { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Args { get; set; }
}
