using System;
using System.Linq;

namespace StreamDak.ViewModels;

// Balayage horizontal de la grille : page suivante ou précédente du profil (ADR 0036). Le geste est reconnu par SwipeGesture (Core) et suivi par
// MainWindow.Touch.cs ; ce fichier dit s'il est permis et change de page par la commande des onglets, sans dupliquer la navigation.
public partial class MainViewModel
{
    /// <summary>Balayage activé (<see cref="DeckConfig.SwipePagesDisabled"/> inversé, relu à chaque reconstruction de la grille).</summary>
    public bool SwipeEnabled { get; set; } = true;

    /// <summary>true si un balayage peut commencer : réglage actif, hors édition (le glisser-déposer y a la priorité), ni menu, ni dialogue, ni bouton soulevé.</summary>
    public bool CanSwipe => SwipeEnabled && !IsEdit && !MenuOpen && !HasDialog && !IsLifting && !IsDragging;

    /// <summary>Onglet de la page voisine dans ce sens (+1 suivante, -1 précédente), ou null au bout de la liste. Les pages masquées n'ont pas d'onglet.</summary>
    TabViewModel? SwipeTarget(int direction)
    {
        int active = Tabs.ToList().FindIndex(t => t.IsActive);
        int next = active + Math.Sign(direction);
        return active < 0 || direction == 0 || next < 0 || next >= Tabs.Count ? null : Tabs[next];
    }

    /// <summary>true si un balayage vers le bas peut revenir au dossier parent (dans un dossier, balayage permis).</summary>
    public bool CanSwipeBack => CanSwipe && InFolder;

    /// <summary>Revient au dossier parent (même chemin que le bouton « Retour »). Faux hors d'un dossier ou si le balayage n'est pas permis.</summary>
    public bool SwipeBack()
    {
        if (!CanSwipeBack) return false;
        FolderUp();
        return true;
    }

    /// <summary>true s'il y a une page dans ce sens (sinon la grille résiste et revient en place : pas de bouclage).</summary>
    public bool HasSwipeTarget(int direction) => SwipeTarget(direction) != null;

    /// <summary>
    /// Passe à la page voisine du profil par la commande de son onglet (même chemin qu'un toucher sur l'onglet ; depuis un dossier ou une sous-page, c'est la
    /// page voisine du profil qui s'ouvre). Faux au bout de la liste ou si le balayage n'est pas permis.
    /// </summary>
    public bool SwipePage(int direction)
    {
        if (!CanSwipe || SwipeTarget(direction) is not { } tab || !tab.Command.CanExecute(null)) return false;
        tab.Command.Execute(null);
        return true;
    }
}
