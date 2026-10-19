using System.Collections.Generic;

namespace TouchDeck;

public class DeckConfig
{
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
    public List<DeckPage> Pages { get; set; } = new();
}

public class DeckPage
{
    public string Name { get; set; } = "Page";
    /// <summary>Page non listée dans la barre d'onglets (accessible via une action "page").</summary>
    public bool Hidden { get; set; }
    public List<DeckButton> Buttons { get; set; } = new();
}

public class DeckButton
{
    public string Label { get; set; } = "";
    /// <summary>Emoji ou court texte affiché au-dessus du libellé.</summary>
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "#2B2F3A";
    public List<DeckAction> Actions { get; set; } = new();
}

public class DeckAction
{
    /// <summary>hotkey | text | media | launch | script | url | page | delay | focus (Value = programme ou titre ; Args = programme à lancer si absent)</summary>
    public string Type { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Args { get; set; }
}
