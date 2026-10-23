# Google Workspace

Connecteur `StreamDak.Integrations.Google` : Google Sheets, Google Docs et Google Agenda, avec **ton propre projet Google Cloud**.
StreamDak n'embarque aucune application Google, ne passe par aucun serveur intermédiaire et ne demande que trois droits :
`spreadsheets`, `documents`, `calendar.events` (ni Gmail, ni Drive complet).

## Préparation (une fois, ≈ 10 minutes)

1. [console.cloud.google.com](https://console.cloud.google.com) › nouveau projet.
2. **API et services › Bibliothèque** : activer *Google Sheets API*, *Google Docs API*, *Google Calendar API*.
3. **Écran de consentement OAuth** : type **Externe**, ajoute ton adresse comme utilisateur test, puis **publie l'application (Production)**.
   Tant qu'elle reste en mode *Test*, Google invalide la connexion **tous les 7 jours** ; en production, sans vérification, l'écran « application non
   vérifiée » apparaît à la connexion et le plafond est de 100 utilisateurs, sans conséquence pour un usage personnel.
4. **Identifiants › Créer des identifiants › ID client OAuth › Application de bureau**. Copie l'ID client et le secret client.
5. StreamDak : **Paramètres › Intégrations › Google Workspace** : colle l'ID client et le secret, puis **Se connecter à Google**.
   Le navigateur s'ouvre (redirection locale `127.0.0.1`, PKCE) ; accepte *Paramètres avancés › Accéder à … (non sécurisé)*.
6. Renseigne l'identifiant du tableur et du document (la partie de l'adresse entre `/d/` et `/edit`), puis **Tester la connexion**.

Seul le **jeton de renouvellement** est conservé, chiffré pour ton compte Windows (`secrets.dat`) ; le secret client l'est aussi.
Rien n'est écrit dans `config.json` hors identifiants non secrets, ni dans le journal.

## Actions

| Commande | Argument | API |
|---|---|---|
| `sheet-append` | valeurs séparées par `;` — `Café;{datetime};12` | `values:append` sur la plage choisie |
| `sheet-set` | `cellule=valeur` — `B2=Terminé le {date}` | `values.update` |
| `doc-append` | texte | `documents:batchUpdate` (`insertText` en fin de document) |
| `calendar-add` | langage naturel — `Dentiste demain à 15h` | `events.quickAdd` (agenda principal) |

`{date}`, `{time}` et `{datetime}` sont remplacés par l'instant du clic. Les valeurs sont interprétées comme si tu les tapais (`USER_ENTERED`).

## Si ça échoue

- « L'autorisation Google a expiré ou été révoquée » : reconnecte-toi ; si c'est hebdomadaire, le projet est resté en mode *Test* (étape 3).
- « Google refuse l'ID client ou le secret » : recopie-les depuis la console.
- « n'a pas renvoyé d'autorisation durable » : révoque StreamDak sur [myaccount.google.com/permissions](https://myaccount.google.com/permissions) puis recommence.

## Widget agenda

Une fois connecté, l'intégration lit l'agenda principal toutes les 5 minutes (`events.list`, 14 jours, droit `calendar.events` déjà demandé) et publie
`calendar.events` (JSON) ou `calendar.error`. Le widget *Agenda* du deck les affiche ; il n'y a rien de plus à régler. Une lecture en échec
n'empêche pas les actions Sheets, Docs et Agenda.

## Limites

Flux joué contre un faux Google (`GoogleTests`) : le comportement exact de l'écran de consentement et l'expiration à 7 jours sont à confirmer
chez toi (**à vérifier**). Pas de lecture des cellules ni d'état dans les titres pour l'instant (les événements sont lus pour le widget agenda, **à vérifier** avec un vrai compte) ; Gmail et Drive complet sont exclus volontairement.
