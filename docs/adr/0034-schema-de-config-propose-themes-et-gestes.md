# 0034 — Schéma de `config.json` proposé : thèmes personnalisés, délai des pages web, déplacement au toucher

- **Statut** : **acceptée le 5 novembre 2026** (schéma validé par l'utilisateur, qui a aussi demandé le déplacement par appui long **activé par défaut**) et implémentée.
- **Date** : 5 novembre 2026.

> Décisions : schéma tel que proposé ; déplacement par appui long activé par défaut (`LiftDragDisabled` à `true` pour le couper). Un site qui doit rester actif 24 h/24 sans premier toucher
> se règle par le mode **Toujours active (24 h/24)** (`input: interactive`, ADR 0025/0032), indépendant du délai ci-dessous.

## Contexte

Trois fonctions codées ou prévues ont besoin de réglages persistants (ADR 0032, 0033 et le thème TIXIA). Les consignes du dépôt demandent de valider tout changement du format avant de l'écrire.

La configuration actuelle porte le thème dans la clé racine `Theme` (chaîne : `system`, `dark`, `light`, `amoled`) ; `ConfigNormalizer` remplace toute autre valeur par `system`.
Il n'existe pas d'objet `Settings.Theme` : la proposition ci-dessous s'adapte à la forme réelle plutôt que de créer un objet parallèle.

## Proposition

Toutes les clés sont **facultatives**, avec `JsonIgnore(WhenWritingDefault)` ou `WhenWritingNull` : un fichier qui ne les utilise pas reste identique octet pour octet. `Version` reste à 2. Aucune clé existante n'est renommée ni supprimée, donc aucune migration.

| Clé (racine) | Type | Défaut (rien d'écrit) | Rôle |
|---|---|---|---|
| `Theme` (existante) | chaîne | `system` | **Valeur acceptée en plus : `tixia`.** Seul changement : la liste blanche de `ConfigNormalizer`. |
| `ThemeOverrides` | objet `{ "accent": "#RRGGBB", "background": …, "surface": …, "text": …, "success": …, "danger": … }` | absent (`null`) | Couleurs choisies par l'utilisateur par-dessus le thème. Une clé absente = couleur du thème ; « Réinitialiser » supprime la clé. Valeurs validées (`#RRGGBB`), les autres ignorées. |
| `WebIdleExitSeconds` | entier | `0` (= 30 s) | Délai sans geste avant qu'une page web active redevienne passive, de 5 à 300. |
| `LiftDragDisabled` | booléen | `false` (= geste activé) | Désactive le déplacement par appui long hors édition. |
| `LiftDelayMs` | entier | `0` (= 500 ms) | Seuil de l'appui long, de 300 à 1500. |

## Compatibilité

- Ancien fichier → nouvelle version : lu tel quel (valeurs par défaut). Un test relira un fichier de la version 1.2.1.
- Nouvelle version → ancienne : les clés inconnues sont ignorées ; `Theme: "tixia"` est ramené à `system` par la version précédente (le thème retombe sur celui de Windows).

## À trancher

1. Valider ou modifier ce schéma (noms des clés, plages, défauts).
2. Faut-il que le déplacement par appui long soit **activé par défaut** ? Il change le geste historique (appui long = éditeur, désormais au relâchement s'il n'y a pas de glissé). Sinon : `LiftDragEnabled` (faux par défaut).
3. Éditeur de couleurs des Paramètres (liste de thèmes avec aperçu, six couleurs, réinitialisation par couleur et globale, aperçu en direct sur de vraies `TileFace`, avertissement de contraste) : à construire **après** validation, car il écrit `ThemeOverrides`.
