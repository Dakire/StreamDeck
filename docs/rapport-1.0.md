# Rapport : première version publiable (26 octobre 2026)

Point de départ `bd9ea9b` (662 tests, 0 avertissement). Arrivée : **1 002 tests au vert, 0 avertissement** (`AnalysisModeSecurity=All`). Un commit par étape.

## 1. Bugs visuels corrigés

Méthode : rendus hors écran du harnais (sombre, clair, AMOLED, portrait, 1080p, édition, paramètres, widgets) relus un par un ; vérification des liaisons de données
(aucune erreur) et du gel des ressources (pinceaux et images : déjà gelés et mis en cache).

| Défaut | Correction |
|---|---|
| Pastille « Simulation » jaune pâle sur fond clair, illisible | couleur propre au thème, contraste ≥ 4,5 vérifié par test |
| « Modifier » d'une touche étroite recouvrait le badge d'étapes | le mot disparaît sous 150 DIP, le crayon reste |
| Test de rendu des jauges dépendant de l'ordre des tests | attend le premier relevé |

**Non vérifié** : DPI 100/125/150/200 % réels, multi-écrans réels et écran tactile (le harnais rend à 96 ppp ; les tailles de fenêtre équivalentes aux DPI ont été rendues, pas les
polices à l'échelle). À essayer à la main. Une limite connue reste : sur une touche très étroite, un titre posé « en haut » peut toucher les badges (essai de correction abandonné : il
coupait les mots).

## 2. Widgets

`IWidget` + `WidgetRegistry` + enregistrement par injection ; disque, réseau, ping, état de services (HTTP / TCP), météo, prochain événement, compteur JSON ; tailles 1 × 1, 2 × 1, 2 × 2 ;
intervalle propre (réglable) ; arrêt quand la page n'est pas affichée et quand le deck est masqué. Décisions simples : horloge, processeur et mémoire gardent leurs tuiles (ADR 0017, 0018) ;
le « compteur de tickets » devient un compteur JSON générique sans authentification (un système protégé passe par une intégration à secret). ADR 0019.

## 3. Badges de notifications

Gmail, Google Chat, Discord, Teams, Slack + fournisseur générique (`notifications.json` : regex de titre, sélecteur CSS). Pages WebView2 cachées, lecture sur événement, mise en veille deck masqué.
Décision simple : **session partagée avec les pages web du deck** (se connecter une fois dans une page web du service), pas une session par service. **Non vérifié** : les titres réels des cinq
services (pas de compte) ; ils sont testés avec de vraies pages WebView2 locales. ADR 0020.

## 4. Sécurité (détail : `SECURITY.md`)

- Aucun secret dans le code ni dans **tout l'historique git** (une constante de test factice) ; aucun paquet vulnérable ; aucune télémétrie ; aucun `Assembly.Load` hors plugins locaux.
- Corrigé : injection possible par le chemin ou les arguments d'un script `.ps1` / `.bat` ; `ExecutionPolicy Bypass` remplacé par `RemoteSigned` ; journal masqué ; secret en clair migré.
- Déjà en place et conservé : import borné et confirmé, DPAPI, canal nommé utilisateur seul, pas de désérialisation polymorphe.
- Reste : certificat de code (non signé), binaires de deux anciens commits, actions GitHub à épingler, plugins sans liste blanche.

## 5. Stabilité et performance

Mesure : application Release `--simulate`, dossier jetable, fenêtre, 30 s au repos, médiane de 2 à 3 lancements, machine de 22 cœurs.

| Cas | Avant | Après |
|---|---|---|
| Deck sans widget système, visible | 125 ms / 30 s (0,02 % de la machine) | inchangé |
| Deck avec horloge + widget Processeur, visible | 922 ms / 30 s (0,14 %) | inchangé (la jauge se redessine chaque seconde) |
| **Même deck, réduit dans la barre des tâches** | **1 766 ms / 30 s** | **250 ms / 30 s (−86 %)** |
| Mémoire de travail | 271 Mo | 271 à 277 Mo |
| Démarrage jusqu'à la fenêtre | 1,6 s | 1,6 s |

La cible « CPU au repos < 1 % » est tenue (≤ 0,2 % de la machine, soit ≈ 4 % d'un cœur avec un widget système affiché). Une mesure a écarté une fausse piste : le relevé du média
Windows ne coûtait presque rien (≈ 1 ms), le coût vient du redessin de la jauge chaque seconde. **Non fait** : virtualisation de la grille (une grille de 32 touches se met à jour en 12 ms,
aucun gain mesurable à attendre) ; passage à `Microsoft.Extensions.Hosting` (ADR 0007 : 40 ms de démarrage en plus pour aucun service de fond) ; async partout (déjà le cas pour les entrées-sorties
du moteur d'actions).

## 6. Installateur

Inno Setup (justification : `docs/INSTALLATION.md`). `build.ps1` : deux variantes win-x64 (self-contained 64 Mo, framework-dependent 11 Mo), WebView2 de Microsoft vérifié, `checksums.txt`.
**Vérifié sur cette machine** : installation silencieuse, lancement de l'application installée (2 variantes), mise à jour sur place pendant que l'application tourne (fermée proprement, même
dossier), désinstallation (fichiers, raccourcis, clé de registre supprimés, `%AppData%` intact). Outil installé pour cela : Inno Setup 6.7.3 (winget, utilisateur courant).

## 7. Mises à jour GitHub

Voir ADR 0021 et `SECURITY.md`. 150 tests dont l'applicateur PowerShell réel (succès, échec avec restauration, attente de fermeture), la signature réelle (`WinVerifyTrust`), la reprise
après coupure et les redirections refusées. **Non vérifié** : une vraie release (le dépôt n'en a pas) ; le workflow GitHub Actions n'a pas tourné.

## Points restant à traiter

1. Certificat de code (puis `-Sign` en CI) ; 2. publier une première release (`v1.0.0`) pour exercer le workflow et la mise à jour de bout en bout ; 3. essais réels : tactile, multi-écrans, DPI,
titres des services de notification ; 4. épingler les actions GitHub ; 5. retirer les anciens binaires de l'historique git (réécriture d'historique : décision du propriétaire).
