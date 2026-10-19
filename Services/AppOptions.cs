namespace TouchDeck;

/// <summary>Options de la ligne de commande.</summary>
/// <param name="Simulate">true : aucune touche envoyée, aucun programme lancé, les actions sont affichées (essais sans risque).</param>
/// <param name="OpenEditor">true : ouvre aussi l'éditeur au lancement.</param>
public sealed record AppOptions(bool Simulate = false, bool OpenEditor = false)
{
    /// <summary>Lit <c>--simulate</c> et <c>--edit</c> (sans distinction de casse).</summary>
    public static AppOptions FromArgs(string[] args)
    {
        bool Has(string name) => System.Array.Exists(args, a => string.Equals(a, name, System.StringComparison.OrdinalIgnoreCase));
        return new AppOptions(Has("--simulate"), Has("--edit"));
    }
}

/// <summary>Configuration chargée au démarrage et message éventuel à afficher à l'utilisateur (migration, fichier invalide).</summary>
/// <param name="Config">Configuration normalisée, ou la configuration par défaut si le fichier était invalide.</param>
/// <param name="Message">Texte à afficher dans la barre d'état du deck, ou null.</param>
public sealed record StartupResult(DeckConfig Config, string? Message);

/// <summary>Grille automatique du deck pour l'écran actuel : l'éditeur s'en sert pour les profils en format « Automatique ».</summary>
public sealed class GridState
{
    /// <summary>Colonnes et lignes que le deck affiche en format automatique.</summary>
    public (int Columns, int Rows) Auto { get; set; } = (5, 4);
}
