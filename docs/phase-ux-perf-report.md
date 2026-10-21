# Bilan : création rapide, simplicité d'usage, performance, nettoyage

Branche `main`, 21 octobre 2026. Point de départ : `d892c27`. Aucune clé de `config.json` ajoutée ni modifiée.
`dotnet build -c Release` : 0 avertissement. Tests : 395 au départ, **501** à l'arrivée, tous au vert.

## 1. Création rapide d'un bouton

| Besoin | Réalisation | Test |
|---|---|---|
| Menu d'une case vide (clic droit, appui long) | `Nouveau bouton…`, `Parcourir les actions`, 9 modèles en un clic, `Coller`, `Éditeur complet…` ; décrit en `MenuItemSpec` | `DeckMenuTests` |
| Menu d'un bouton | `Modifier…`, `Dupliquer`, `Copier`, `Couper`, `Supprimer`, `Éditeur complet…` | `DeckMenuTests` |
| Panneau rapide | mini-fenêtre : recherche dans tout le catalogue, titre et icône proposés, `Créer` (Entrée) / `Annuler` (Échap) de 48 px ; brouillon, aucune écriture à la frappe | `QuickCreatePanelTests` |
| Glisser-déposer | fichier, `.lnk`, `.exe`, dossier, `.url`, adresse web ; icône de l'application ; plusieurs éléments = une annulation | `DropTests`, `QuickCreateTests` |
| Duplication | dans la prochaine case libre ; sur page pleine, dans la sous-page suivante | `DeckMenuTests` |
| Annuler / Rétablir | un pas par création ; bouton `Annuler` dans le bandeau du deck ; Ctrl+Z / Ctrl+Y et boutons dans l'éditeur | `DeckMenuTests` |

Cas testés : création par menu, modèle, panneau, dépôt ; annulation et rétablissement ; case occupée (jamais écrasée) ; page pleine ;
entrées invalides (vide, chemin illégal, `javascript:`, `file:`, texte quelconque) ; aucune erreur de liaison de données (harnais).
Le test qui garantit `WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE` et `ShowActivated=false` passe ; `MainWindow.xaml.cs` n'a pas été modifié.

Écarts et points d'attention :
- **Menu affiché dans la feuille du deck, pas par `ContextMenuFactory`** (décision validée) : un `ContextMenu` WPF est un popup séparé,
  hors du traitement tactile du deck (ADR 0013). `ContextMenuFactory` reste utilisé par l'éditeur. ADR 0014.
- **Panneau en deux parties** (décision validée) : parcours tactile dans le deck, saisie dans une mini-fenêtre activable.
  **Non vérifié sur le bureau réel** (consigne : ne pas le piloter) : que Windows rende bien le focus à l'application précédente
  à la fermeture du panneau. À essayer à la main avec le Bloc-notes.
- **La touche Menu / Maj+F10 n'ouvre rien** : le deck n'a jamais le focus clavier.
- **Clic droit sur un bouton = menu** (décision validée) : un toucher de plus pour modifier qu'avant (`Modifier…`).
- « Page pleine » : le document n'a pas de plafond, l'ajout réussit en sous-page (hypothèse du plan).

## 2. Simplicité d'usage

| Friction | Correction | Test |
|---|---|---|
| Premier lancement ou page vide : grille « éteinte », rien n'invite à agir | Première case : `Ajouter un bouton` (ouvre le menu de création), sans fenêtre imposée | `FirstRunTests` |
| Le clic droit et l'appui long ne se découvrent pas | Indice unique par lancement, seulement si la page a 2 boutons ou moins, jamais sur un autre message ; non persistant (pas de clé) | `FirstRunTests` |
| « Délai dépassé », « Type d'action inconnu » : disent quoi, pas quoi faire | Messages complétés ; un échec propose `Modifier` dans le bandeau | `ExecutionFeedbackTests`, `ActionEngineTests` |
| « action » désigne parfois le bouton (menu de l'éditeur) | `Dupliquer / Déplacer / Supprimer le bouton` ; « action » reste pour le catalogue | `UiTests` |
| Éditeur : 5 blocs de réglages visibles dont 3 d'apparence | Aperçu, titre, action d'abord ; apparence repliée (`Modifier l'icône` la déplie) | `UiTests`, rendu |
| Contrôles sans nom pour un lecteur d'écran | Audit automatique du deck, de l'éditeur, des paramètres, du panneau : aucun contrôle sans nom | `AccessibilityTests` |
| Contrastes des nouvelles surfaces | Contrastes ≥ 4,5 vérifiés en sombre, AMOLED et clair | `DesignSystemTests` |

Non traité, à décider : « Étape » reste le mot de l'éditeur pour une ligne d'un enchaînement (renommer en « Action » créerait une
collision avec le catalogue) ; le champ « Valeur » n'existe déjà plus, chaque type a son libellé (« Combinaison », « Programme »…).
Aucun changement visuel majeur n'a été implémenté : la disposition de l'éditeur a seulement changé d'ordre, l'identité (palette,
AMOLED, 48 px) est intacte.

## 3. Performance

Méthode : application réelle en `--simulate`, configuration jetable, fenêtre non plein écran, 4 lancements par série, deux séries
en alternance, médianes ; CPU sur 30 s au repos. Version de départ construite à part (`git archive d892c27`).

| Mesure | Départ `d892c27` | Après les fonctionnalités | Remarque |
|---|---|---|---|
| Démarrage (médiane, 2 séries) | 766 / 731 ms | 725 / 690 ms | pas de régression (écart dans le bruit) |
| Mémoire de travail | 168 / 154 Mo | 168 / 154 Mo | varie de 14 Mo entre séries sans changement de code : bruit système |
| Mémoire privée | 109,5 / 105 Mo | 105 / 108 Mo | identique |
| CPU au repos, 30 s | 78 / 47 ms | 47 / 47 ms | au plancher (≈ 0,15 %) |

### Gains retenus (mesurés)

| Optimisation | Avant | Après | Mesure |
|---|---|---|---|
| **Fenêtres fermées retenues** (barre de titre WPF-UI abonnée à un événement statique) | 42,1 Mo retenus pour 50 ouvertures de l'éditeur (863 Ko par ouverture) | 0,7 Mo (15 Ko par ouverture) | test `FiftyOpenedAndClosedEditorsLeaveAlmostNothingBehind` |
| **ReadyToRun à la publication** | 711 / 705 / 715 ms | 632 / 652 / 625 ms (−11 %) | 3 séries de 8 lancements en alternance ; mémoire inchangée |
| `config.json` non réécrit si identique | une écriture et un rechargement filtré à chaque sauvegarde | aucune | `SavingUnchangedContentDoesNotRewriteTheFile` (gain disque, non chiffré en ms) |

### Examinés et écartés (gain nul ou non mesurable)

| Idée | Mesure | Décision |
|---|---|---|
| Connecteurs d'intégration paresseux | création des 12 connecteurs : 30-37 ms (≈ 4 % du démarrage), lecture des identifiants : 3-4 ms ; bruit ± 25 ms | écarté : gain sous le bruit, risque sur catalogue et validation |
| `TieredPGO=0` | 709 et 720 ms contre 727 et 704 ms | écarté : bruit |
| `TC_QuickJitForLoops=0` | +90 ms (dégradation) | écarté |
| Minuteur de 0,8 s (profil par application) conditionnel | CPU au repos déjà à 47 ms par 30 s | écarté : rien à gagner |
| Debounce de la recherche du catalogue | 0,26 ms médiane par frappe (153 actions) | écarté : inutile |
| Cache d'images borné, `Freeze` | `Freeze` et `OnLoad` déjà en place | rien à faire |
| Journal Serilog | déjà 1 Mo × 3 fichiers | rien à faire |
| Fuite par changement de page | 100 passages, nombre de tuiles stable | rien à corriger |

## 4. Nettoyage

- Aucun paquet NuGet inutilisé (`dotnet list package`) ; plus de `using` inutile (IDE0005 bloque la compilation).
- `EditorViewModel` 1140 → 911 lignes (menus et glisser-déposer en classes partielles), `DeckDocument` 716 → 515 (pages et profils).
  `MainViewModel` reste à 664 lignes plus deux fichiers partiels (menu des cases, glisser-déposer) : le reste forme un seul tout.
  `EditorWindow.xaml` (839 lignes) n'a pas été découpé : aucun bloc de ressources ne s'en détache proprement.
- La création rapide réutilise `DeckDocument`, `ActionCatalog.CreateButton` et un utilitaire de menu partagé (`CatalogMenu`) ;
  la duplication de code entre éditeur et deck est supprimée.
- `ArchitectureTests` au vert : `QuickCreate` et `DroppedItem` sont dans le Core, sans WPF.
- Docs : README, CHANGELOG, ARCHITECTURE, ADR 0014.

## Décisions qui vous attendent

1. **Décoder les images à la taille d'affichage** : 32 images de 512 px coûtent jusqu'à ≈ 42 Mo de mémoire privée (majorant). Décoder
   à 256 px diviserait cela par 4 mais rendrait les touches floues en plein écran 4K. À valider avec un essai visuel.
2. **Ombres des touches** (3 `DropShadowEffect` par touche : rendu logiciel 119 ms contre 46 ms sans, chiffres de `cleanup-report.md`) :
   changement visuel, non appliqué.
3. **Mémoriser l'indice de découverte** : aujourd'hui une fois par lancement. Une clé `config.json` (valeur par défaut, `JsonIgnore`) le
   rendrait définitif ; non ajoutée sans votre accord.
4. **Libellé « Étape »** : voir §2.
5. **Vérification manuelle** du retour du focus après fermeture du panneau rapide, sur votre bureau.
