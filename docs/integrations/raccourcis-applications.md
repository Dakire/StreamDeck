# Raccourcis d'applications

Catégorie « Applications cibles » du catalogue : des boutons prêts à poser pour les applications qui n'ont pas d'API locale exploitable.
Chaque bouton ramène la fenêtre de l'application au premier plan (action `focus`, recherche par titre), attend 150 ms (`delay`),
puis envoie le raccourci (`hotkey`). Discord fait exception : ses raccourcis sont globaux, aucune mise au premier plan ; mieux encore, l'[intégration Discord](discord.md)
se passe de raccourci et fonctionne même derrière un jeu lancé en administrateur.

| Application | Boutons |
|---|---|
| Zoom | micro (Alt+A), caméra (Alt+V), lever la main (Alt+Y), partage (Alt+Maj+S), quitter (Alt+Q) |
| Teams | micro (Ctrl+Maj+M), caméra (Ctrl+Maj+O), main (Ctrl+Maj+K), partage (Ctrl+Maj+E), raccrocher (Ctrl+Maj+H) |
| Discord | micro et sourdine (Ctrl+Alt+Maj+M / D, à déclarer dans Discord comme raccourcis globaux : *Toggle Mute*, *Toggle Deafen*) |
| Premiere Pro | lecture, lame, sélection, couper ici, supprimer et combler |
| Photoshop | pinceau, gomme, déplacement, tamponner le visible |
| DaVinci Resolve | lecture, lame, sélection, enregistrer |
| Blender | mode objet / édition, déplacer, rendu, annuler (pointeur au-dessus de la vue 3D) |
| Outlook, Word, Excel | nouveau message, répondre, répondre à tous, enregistrer |
| Star Citizen | une touche libre : à renseigner selon tes assignations |

## Limites

- Aucun état n'est lisible (pas de voyant) : une touche « micro » bascule sans savoir si le micro est coupé.
  Pour un état réel, voir les intégrations OBS, Hue…
- Ce sont les raccourcis par défaut ; s'ils sont modifiés dans l'application, change la combinaison du bouton.
- Le titre de fenêtre recherché peut différer selon la langue ou la version : ajuste le champ « Fenêtre » du bouton.
- Certains jeux ou anti-triche ignorent les touches simulées (`SendInput`). Aucun contournement n'est prévu. Quand l'application au premier plan tourne en
  administrateur, Windows refuse toute touche simulée : la touche le dit (« Windows a bloqué les touches… »).
- Une combinaison envoyée sans mise au premier plan et déjà utilisée ailleurs (Ctrl+Maj+M : Teams) est signalée dans les propriétés du bouton.
- DaVinci Resolve (scripting externe, version Studio), Adobe (plugins UXP) et Blender (add-on) pourraient offrir des états réels : voir `docs/integrations-research.md`.
