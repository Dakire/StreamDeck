# 0006 — Journal maison plutôt qu'une bibliothèque de journalisation

- **Statut** : remplacée par [0008](0008-journal-serilog.md) (phase 2).
- **Date** : 19 octobre 2026.

## Contexte

TouchDeck consigne peu de choses : démarrage, erreurs, actions en échec, changements d'écran. Le cahier des charges
évoque Serilog ; le dépôt n'accepte une nouvelle dépendance NuGet que si elle est justifiée.

## Décision

- **Journal maison.** `Core/Log.cs` reste le journal : un fichier `touchdeck.log` dans le dossier de configuration,
  1 Mo au maximum, l'ancien gardé en `.old`.
- **Jamais d'exception.** Un journal en échec ne doit pas faire tomber le deck.
- **Dans `Core`.** Il y est placé pour que la persistance (`ConfigStore`) puisse journaliser sans dépendre de
  l'interface.

## Conséquences

- Aucune dépendance supplémentaire.
- Pas de niveaux configurables ni de journal structuré.
- Si la phase 2 introduit `Microsoft.Extensions.Hosting`, `ILogger` et un fournisseur fichier pourront remplacer `Log`
  derrière la même façade. Les intégrations à venir devront masquer tout secret avant de journaliser.
