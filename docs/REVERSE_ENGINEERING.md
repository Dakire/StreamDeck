# Rétro-ingénierie fonctionnelle d'Elgato Stream Deck

Objectif : reproduire les **usages** du logiciel Stream Deck sur un écran tactile standard, sans piloter de matériel Elgato,
sans copier de code propriétaire et sans contourner de protection.

## Sources et niveau de confiance

| Marque | Signification |
|---|---|
| **[doc]** | Décrit dans la documentation publique d'Elgato (aide en ligne, SDK sur `docs.elgato.com/streamdeck/sdk`). Connaissance antérieure, **non revérifiée en ligne pendant cette session** : à relire avant d'en dépendre. |
| **[obs]** | Comportement observable de l'application, reproductible. |
| **[hyp]** | Hypothèse ou souvenir non vérifié. |

Aucun binaire Elgato n'a été décompilé ni analysé.

## Modèle du logiciel de référence

| Concept | Comportement de référence | Niveau |
|---|---|---|
| Appareil | Chaque appareil (15, 32, 6 touches, + avec molettes…) a ses profils ; la grille est fixée par le matériel. | [doc] |
| Profil | Ensemble de pages de touches ; exportable en `.streamDeckProfile` ; commutable à la main ou automatiquement selon l'application au premier plan. | [doc] |
| Pages | Plusieurs pages par profil, avec indicateur et navigation (touches « page suivante » / « précédente »). | [doc] |
| Dossiers | Une touche « Dossier » ouvre une sous-grille ; la première touche ramène au parent. Imbrication possible. | [doc] |
| Actions | Identifiant stable en notation inversée (`com.elgato.streamdeck.system.hotkey`), paramètres stockés par instance. | [doc] |
| États | Une action peut avoir deux états (ex. muet / non muet) ; titre et image par état. | [doc] |
| Retour visuel | Le plugin peut afficher une coche (`showOk`) ou un triangle d'alerte (`showAlert`) sur la touche. | [doc] |
| Titre | Texte superposé (haut, milieu, bas), police, taille, couleur, masquable. | [doc] |
| Multi-action | Enchaîne des actions avec délais ; variante « commutateur » à deux séquences alternées. | [doc] |
| Actions système | Raccourci clavier, raccourci à bascule, texte, ouvrir (programme, fichier), site web, multimédia, minuteur. | [doc] |
| Plugins | Exécutable ou Node.js qui dialogue en WebSocket/JSON avec le logiciel (événements `keyDown`, `willAppear`… ; commandes `setTitle`, `setImage`, `setState`). Interface de réglages (*Property Inspector*) en HTML. | [doc] |
| Glisser-déposer | Les actions se posent depuis une bibliothèque latérale vers la grille ; le panneau du bas règle l'action sélectionnée. | [obs] |
| Marketplace | Plugins, icônes et profils distribués en ligne. | [doc] |

## Ce que StreamDak reprend, adapte ou écarte

- **Repris** : profils, pages, dossiers imbriqués, profil automatique par application, multi-actions avec délais,
  titres positionnables, retour coche / alerte, catalogue d'actions avec recherche et glisser-déposer, export / import de profil.
- **Adapté** :
  - la grille n'est pas imposée par un matériel : elle dépend du profil (format fixe) ou de l'écran (automatique) ;
  - les sous-pages sont créées automatiquement quand une page dépasse la grille ;
  - le « périphérique », c'est l'écran tactile (branchement et débranchement détectés).
- **Écarté** :
  - compatibilité avec les plugins Elgato : leur protocole WebSocket est public, mais ils supposent le logiciel Elgato
    (manifeste, Property Inspector HTML, cycle de vie) ; rien n'est promis et rien n'est implémenté ;
  - lecture des fichiers `.streamDeckProfile` : format non documenté publiquement **[hyp : archive de manifestes JSON]**,
    non tenté ;
  - Marketplace : aucun code distant n'est exécuté (règle de sécurité du projet).

## Écarts restants notables

1. **États multiples** (touche à bascule avec deux apparences) : pas encore de modèle `States` dans `config.json`.
   Il demande un changement de format, donc une décision explicite. Aujourd'hui, l'action `audio` affiche l'état obtenu (« Micro coupé ») sans changer l'apparence de la touche.
2. **Plugins** : pas de système d'extensions. Voir `FEATURE_PARITY.md` (P2) pour l'approche envisagée.
3. **Molettes / écran tactile horizontal** (Stream Deck +) : sans objet pour un écran tactile classique ; le geste de balayage
   pourrait jouer ce rôle (non implémenté).
