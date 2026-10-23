# Système Windows

Connecteur `StreamDak.Integrations.WindowsSystem` (cible `net10.0-windows10.0.19041.0`, nécessaire à l'API WinRT du média).
Aucun compte, aucun réglage : activer l'intégration suffit (**Paramètres › Intégrations › Système Windows**).

## Valeurs pour les titres

| Jeton | Contenu |
|---|---|
| `{sys.cpu}` | Charge du processeur en % (compteur « Processor Information / % Processor Utility », comme le Gestionnaire des tâches) |
| `{sys.ram}` | Mémoire vive utilisée en % |
| `{sys.ram.used}` / `{sys.ram.total}` | Mémoire utilisée et totale, en Go avec un point décimal (`12.4`) |
| `{sys.media.title}` / `{sys.media.artist}` | Titre et artiste du média en cours, vides si rien ne joue |
| `{sys.media.app}` | Application source (Spotify, Chrome…) |
| `{sys.media.playing}` | `true` ou `false` ; voyant plein sur la touche « Lecture / pause » pendant la lecture |

Exemple : `CPU {sys.cpu} %` ou `{sys.media.title:rien}`. Relevé toutes les secondes.

## Widgets Processeur et Mémoire

Catalogue › *Widgets* › *Processeur* et *Mémoire* : une tuile (2 × 1 par défaut, redimensionnable) avec la valeur en grand et une jauge, mise à jour à chaque
relevé (1 s). Le premier widget affiché active lui-même cette intégration ; sans relevé, la tuile dit pourquoi. La jauge passe en couleur de danger
à partir de 85 %. Pas de serveur ni de WebSocket : les valeurs restent dans le processus (ADR 0018).

## Actions

`playpause`, `next`, `previous` : agissent sur la session média courante de Windows (celle des touches multimédia du clavier),
quelle que soit l'application. Contrairement aux touches `media` simulées, elles signalent clairement qu'aucune application ne lit de média.

## Spotify

Le média en cours de Spotify (titre, artiste, lecture) est lu par ce connecteur : aucun compte Premium ni application développeur n'est nécessaire.
L'API Web de Spotify (playlists, file d'attente) n'est pas prise en charge : voir `docs/integrations-research.md`.

## Limites

- Température, GPU et ventilateurs ne sont pas lus : ils demandent un pilote noyau ou des droits d'administrateur (LibreHardwareMonitor), ce que StreamDak n'exige jamais.
- Windows 10 2004 (build 19041) ou plus récent.
