# Système Windows

Connecteur `TouchDeck.Integrations.WindowsSystem` (cible `net10.0-windows10.0.19041.0`, nécessaire à l'API WinRT du média).
Aucun compte, aucun réglage : activer l'intégration suffit (**Paramètres › Intégrations › Système Windows**).

## Valeurs pour les titres

| Jeton | Contenu |
|---|---|
| `{sys.cpu}` | Charge du processeur en % (compteur « Processor Information / % Processor Utility », comme le Gestionnaire des tâches) |
| `{sys.ram}` | Mémoire vive utilisée en % |
| `{sys.media.title}` / `{sys.media.artist}` | Titre et artiste du média en cours, vides si rien ne joue |
| `{sys.media.app}` | Application source (Spotify, Chrome…) |
| `{sys.media.playing}` | `true` ou `false` ; voyant plein sur la touche « Lecture / pause » pendant la lecture |

Exemple : `CPU {sys.cpu} %` ou `{sys.media.title:rien}`. Relevé toutes les secondes.

## Actions

`playpause`, `next`, `previous` : agissent sur la session média courante de Windows (celle des touches multimédia du clavier),
quelle que soit l'application. Contrairement aux touches `media` simulées, elles signalent clairement qu'aucune application ne lit de média.

## Spotify

Le média en cours de Spotify (titre, artiste, lecture) est lu par ce connecteur : aucun compte Premium ni application développeur n'est nécessaire.
L'API Web de Spotify (playlists, file d'attente) n'est pas prise en charge : voir `docs/integrations-research.md`.

## Limites

- Température, GPU et ventilateurs ne sont pas lus : ils demandent un pilote noyau ou des droits d'administrateur (LibreHardwareMonitor), ce que TouchDeck n'exige jamais.
- Windows 10 2004 (build 19041) ou plus récent.
