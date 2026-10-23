# 0013 — Toucher du deck sans souris système

## Contexte
Un toucher sur l'écran du deck déplaçait le curseur de la souris. Journal de diagnostic (`STREAMDAK_TOUCHLOG=1`) :
- pile WISP (défaut de WPF) : la fenêtre ne reçoit aucun `WM_POINTER`, Windows envoie directement une souris synthétique
  (`WM_MOUSEMOVE` « depuis-tactile ») qui repositionne le curseur ;
- pile pointeur de WPF : `WM_POINTER` reçu, puis `DefWindowProc` promeut le toucher en souris système (même effet) ;
  WPF n'active pas les boutons à partir du toucher, il attend cette souris synthétique.
Impossible donc d'avoir à la fois des boutons actifs et un curseur immobile en laissant WPF faire.

## Décision
- Activer `Switch.System.Windows.Input.Stylus.EnablePointerSupport` (`StreamDak.csproj`) pour recevoir `WM_POINTER`.
- Dans le hook de `MainWindow`, avaler les `WM_POINTER` de type doigt ou stylet (`handled = true`) : pas de promotion en souris.
- Traiter l'appui dans la fenêtre (`MainWindow.Touch.cs`, `TapRecognizer`) : test de collision, activation du bouton par
  l'automatisation WPF (`IInvokeProvider`), commande d'un `MouseBinding` (fermeture du menu), barre du haut déplaçable, double appui.
- Donner quand même le message à l'entrée de WPF (`HwndSource.InputFilterMessage`, interne, par réflexion) pour la manipulation ;
  si elle est introuvable, un avertissement est journalisé et seul le défilement tactile est limité.
- Aucun `SetCursorPos`.

## Conséquences
- Le curseur ne bouge plus ; la souris (PT_MOUSE) n'est jamais filtrée.
- Pas d'état « enfoncé » pendant l'appui, pas de clic droit sur appui long (non utilisé par le deck).
- L'éditeur et les paramètres gardent le comportement WPF par défaut.
- Dépendance à un membre interne de WPF pour le défilement : à revérifier à chaque montée de version majeure.
