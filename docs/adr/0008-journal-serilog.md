# 0008 — Journal Serilog derrière une façade du Core

- **Statut** : acceptée. Remplace [0006](0006-journal-maison.md).
- **Date** : 19 octobre 2026.

## Contexte

Le journal maison suffisait tant qu'il n'y avait que quelques messages. Les intégrations à venir (OBS, Twitch, Hue…)
demandent des niveaux, un format stable, des exceptions complètes et la possibilité de masquer des secrets avant écriture.
Le cahier des charges prévoyait Serilog, la dépendance a été validée.

## Décision

- **Façade dans le Core.** `Core/Log.cs` garde une API minimale (`Info`, `Warn`, `Error`) et délègue à un `ILogSink`. Le
  Core ne dépend d'aucune bibliothèque de journalisation. Sans destination, les messages sont ignorés.
- **Serilog dans l'application.** `AppHost` crée un logger Serilog (fichier `streamdak.log` du dossier de configuration,
  1 Mo par fichier, 3 fichiers conservés) et branche la façade dessus ; `ILogger<T>` est disponible pour les services injectés
  (`Serilog.Extensions.Logging`).
- **Tests.** Une destination en mémoire (`MemoryLogSink`) remplace la façade le temps d'un test.
- **Jamais d'exception.** Un journal qui échoue ne fait pas tomber le deck : `Log` capture l'échec de sa destination.

## Conséquences

- Deux dépendances : `Serilog.Sinks.File` et `Serilog.Extensions.Logging`.
- Le format de fichier change : `streamdak_001.log` remplace `streamdak.log.old` (documenté dans `configuration.md`).
- Les messages issus de `Log` et de `ILogger<T>` arrivent dans le même fichier (test `CompositionTests`).
- Règle pour les intégrations : ne jamais journaliser un jeton ni un mot de passe, y compris dans une exception.