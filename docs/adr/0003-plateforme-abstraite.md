# 0003 — Appels système derrière des interfaces, plateforme simulée

- **Statut** : acceptée.
- **Date** : 2026.

## Contexte

Les actions envoyaient des touches, lançaient des programmes et pilotaient l'audio par des appels Win32 statiques, dans un
`switch`. Ce code n'était pas testable sans agir sur le vrai bureau. Il n'avait ni délai maximal ni annulation.

## Décision

- **Interfaces.** Les actions n'appellent le système qu'à travers `IKeyboardService`, `IAppLauncher`, `IWindowService` et
  `IAudioService` (`Core/Actions/Platform.cs`).
- **Implémentation Windows.** Elle est dans `Platform/Windows/WindowsPlatform.cs`. Les P/Invoke restent dans
  `NativeInput.cs`, `WindowFocus.cs`, `NativeAudio.cs` et `MainWindow.xaml.cs`.
- **Plateforme simulée.** `SimulatedPlatform` consigne au lieu d'exécuter. Elle sert aux tests et au mode `--simulate`.
- **Moteur.** `ActionEngine` exécute les étapes avec un délai maximal par étape et une annulation. Il ne lève jamais
  d'exception : le résultat est un `ActionResult`.

## Conséquences

- Les actions sont testées sans toucher au bureau (`ActionEngineTests`, `DeckNavigationTests`).
- Le harnais de tests de l'interface utilise la plateforme simulée. Un test garantit qu'il ne bascule jamais sur la
  plateforme réelle ni n'affiche de fenêtre : un défaut de démarrage l'avait fait jusqu'au nettoyage de la phase 1.
- Une autre plateforme (Linux) n'aurait qu'à fournir ces quatre interfaces.
