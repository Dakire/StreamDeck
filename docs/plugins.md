# Écrire un plugin

Un plugin ajoute une intégration (commandes, valeurs pour les titres, voyants) sans modifier TouchDeck.
L'exemple complet est dans [`samples/HelloPlugin`](../samples/HelloPlugin).

## En bref

1. Créer une bibliothèque `net9.0` qui référence `TouchDeck.Core` (`EnableDynamicLoading` activé, Core non copié).
2. Écrire une classe **publique**, avec un constructeur **sans paramètre**, qui dérive de `IntegrationBase`
   (ou de `PolledHttpIntegration` pour un appareil HTTP) : `Id`, `Name`, `Category`, `Help`, `Fields`, `Commands`,
   `ConnectCoreAsync`, `DisconnectCoreAsync`, `ExecuteAsync`.
3. Compiler, puis copier `NomDuPlugin.dll` (et son `.deps.json`) dans `%APPDATA%\TouchDeck\plugins\NomDuPlugin\` : **le dossier et la
   bibliothèque portent le même nom**.
4. Relancer TouchDeck. Le journal (`touchdeck.log`) indique « Plugin … chargé » ou la raison du refus.
5. Activer l'intégration dans **Paramètres › Intégrations**. Ses commandes apparaissent dans le catalogue.

## Règles

- L'identifiant (`Id`) doit être unique : un identifiant déjà pris par TouchDeck ou un autre plugin fait ignorer la classe.
- Publier l'état avec `SetValue` / `SetValues` ; signaler une panne avec `ConnectionLost` (TouchDeck se reconnecte avec une attente croissante).
- Lever des exceptions à message clair (« Indique le nom de la scène ») : elles s'affichent telles quelles sur le deck.
- Les secrets passent par un champ `SettingKind.Secret` : chiffrés (DPAPI), jamais dans `config.json` ni dans le journal.
- Une dépendance NuGet du plugin se copie à côté de la bibliothèque ; elle est chargée dans le contexte du plugin, sans conflit avec TouchDeck.

## Sécurité

**Un plugin s'exécute avec tes droits.** Ne pose dans `plugins` que du code que tu as écrit ou que tu as relu. TouchDeck ne télécharge
rien, n'exécute rien venant du réseau, et journalise l'empreinte SHA-256 de chaque plugin chargé pour qu'un changement se voie.
Le plugin est verrouillé tant que TouchDeck tourne : quitter l'application pour le mettre à jour.
