# 0019 — Widgets de données extensibles (`IWidget`)

- **Statut** : acceptée.
- **Date** : 26 octobre 2026.

## Contexte

Les widgets de l'ADR 0017 (horloge, agenda, page web) et 0018 (processeur, mémoire) ont chacun une classe de tuile et un modèle XAML
écrits à la main. Il faut pouvoir en ajouter vite (disque, réseau, ping, météo, état de services, prochain événement, compteur), avec :
une taille 1 × 1, 2 × 1 ou 2 × 2, un intervalle de rafraîchissement propre, aucun relevé tant que leur page n'est pas affichée, et une
inscription par injection de dépendances.

## Décision

**Contrat** (`Core/Widgets/IWidget.cs`, sans WPF) : `IWidget` = une description (`WidgetInfo` : identifiant, nom, icône, tailles permises,
réglages, intervalle par défaut) et `ReadAsync(WidgetContext, CancellationToken)`, qui rend un `WidgetReading` (valeur en grand, détail, jauge
facultative, gravité `Normal | Good | Warning | Danger`). Une erreur est une exception au message affichable en français : la tuile l'affiche à la
place de la mesure.

**Un seul modèle visuel** pour tous les widgets de données (`DataWidgetViewModel` + un `DataTemplate`) : en-tête, valeur colorée selon la
gravité, détail multiligne, jauge. Ajouter un widget = une classe `IWidget`, sa description dans `WidgetTypes.All`, une ligne dans
`AppHost.Build`. Un test (`EveryDataWidgetIsDescribedConsistentlyAndRegisteredInTheContainer`) vérifie que description, classe, taille par
défaut, icône, intervalle et entrée du catalogue concordent.

**Réglages génériques** : chaque widget déclare ses réglages texte (`WidgetOption`) ; le panneau de propriétés les affiche sans XAML
supplémentaire, plus un champ « Rafraîchissement (secondes) » commun (clé `interval`, 1 à 3600). Ils sont écrits dans `WidgetOptions`, donc
**aucune nouvelle clé de premier niveau dans `config.json`** : seule la clé `interval` d'option s'ajoute, absente par défaut.

**Cycle de vie** : la grille ne contient que les tuiles de la page affichée. Une tuile démarre son relevé à sa création et s'arrête quand elle
quitte la grille (changement de page, suppression, changement de thème : `MainViewModel.ReleaseDataWidgets`) ou quand la fenêtre se ferme.
Quand le deck est réduit ou masqué (`MainViewModel.SetActive(false)`), toutes les tuiles se suspendent ; au retour, chacune relit tout de
suite, même avec un intervalle de 30 minutes. En mode édition, un widget est une touche : aucun relevé.

**Widgets livrés** : `disk`, `network`, `ping`, `status` (HTTP ou port TCP), `weather` (Open-Meteo), `nextevent` (valeurs de l'intégration
Google), `counter` (nombre lu dans une réponse JSON). L'horloge et la date, le processeur et la mémoire, l'agenda et la page web gardent leurs
tuiles dédiées (ADR 0017 et 0018).

## Sécurité

- Aucun secret : pas de jeton enregistré dans les réglages d'un widget. Le compteur lit une adresse sans authentification ; un système de
  tickets protégé passe par une intégration (secrets DPAPI), pas par ce widget. **Compteur de tickets : généralisé en « Compteur » (optionnel
  demandé), sans connecteur propre à un logiciel de tickets.**
- Réseau : seulement vers ce que l'utilisateur saisit (hôtes de ping, cibles, adresse du compteur), plus `geocoding-api.open-meteo.com` et
  `api.open-meteo.com` (HTTPS, nom de ville seul envoyé) pour la météo. Réponses JSON limitées à 256 Ko et 10 s ; un nom d'hôte n'est accepté
  que s'il ne contient que lettres, chiffres, point, tiret, deux-points et crochets.
- Aucun widget n'exécute de code reçu : les réponses sont lues comme des données.

## Conséquences

- Un widget de données = au plus 1 relevé réseau par intervalle et par tuile affichée, 0 hors page ou deck masqué.
- Les widgets Processeur et Mémoire ne sont pas migrés vers `IWidget` : ils se nourrissent d'une intégration (`ValuesChanged`), pas d'un relevé
  propre. Les migrer serait possible plus tard sans changer `config.json`.
- Non vérifié ici : le rendu au doigt et la météo contre le vrai service (les tests utilisent un faux serveur HTTP et des réponses types).
