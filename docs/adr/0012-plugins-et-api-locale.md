# 0012 — Plugins locaux et API locale par canal nommé

- **Statut** : acceptée.
- **Date** : 20 octobre 2026.

## Contexte

Les connecteurs livrés ne couvrent pas tous les besoins (matériel maison, logiciel de niche). Par ailleurs, des scripts ou
d'autres outils doivent pouvoir piloter le deck (changer de profil, déclencher une action) et une seconde exécution de
StreamDak ne doit pas ouvrir une seconde fenêtre.

## Décision

- **Plugins** : bibliothèques .NET posées par l'utilisateur dans `plugins\<Nom>\<Nom>.dll` du dossier de configuration, contenant des
  classes `IIntegration`. Chaque plugin a son `AssemblyLoadContext` ; `StreamDak.Core` est partagé. Un plugin défectueux est ignoré
  (journal) sans empêcher le démarrage ; un identifiant déjà pris est refusé ; l'empreinte SHA-256 est journalisée.
  **StreamDak ne télécharge jamais de plugin** (règle du dépôt : pas de code issu d'une source distante). Le code d'un plugin s'exécute
  avec les droits de l'utilisateur : la confiance est celle qu'on accorde à tout programme qu'on installe.
- **API locale** : canal nommé `StreamDak.Api.<utilisateur>.<clé>` en `CurrentUserOnly`, une ligne JSON par requête. Aucun port TCP.
  Les actions passent par la même validation et le même moteur que les boutons.
- **Instance unique** par dossier de configuration : verrou `Local\StreamDak.<clé>` ; la seconde instance envoie `show` / `edit` à la première.

## Alternatives écartées

- Serveur HTTP/WebSocket local : surface réseau et risque d'attaque depuis un navigateur (DNS rebinding) ; le canal nommé est limité au compte.
- Chargement à chaud des plugins : un assembly non collectible ne se décharge pas ; le redémarrage de StreamDak suffit et reste simple.
- Bac à sable des plugins : .NET n'offre plus de modèle de confiance partielle ; faire croire à une isolation serait trompeur.

## Conséquences

- Mettre à jour un plugin demande de quitter StreamDak (le fichier est verrouillé).
- Les scripts sans accès aux canaux nommés (WSL, navigateur) ne peuvent pas utiliser l'API : acceptable pour un usage personnel.
