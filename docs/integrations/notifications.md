# Notifications (badges)

Connecteur `StreamDak.Integrations.Notifications` : un **badge rouge** avec le nombre de non lus (« 99+ » au-delà) sur les boutons « site web » de
**Gmail**, **Google Chat**, **Discord**, **Microsoft Teams** et **Slack**, et de tout service que tu décris toi-même. ADR 0020.

## Activer

1. Crée (ou garde) un bouton qui ouvre le site : catalogue › *Sites web*, ou glisse l'adresse sur une case. Le badge se pose sur tout bouton dont une
   étape « Lien web » porte l'adresse du service (`mail.google.com`, `chat.google.com`, `discord.com`, `teams.microsoft.com`, `slack.com`…).
2. **Connecte-toi une fois au service dans le deck** : ajoute une page web ou un widget web du même service (catalogue › Widgets › *Gmail*, *Google Chat*),
   et connecte-toi dans son cadre. Le suivi utilise la **même session** (profil WebView2 de StreamDak). Aucun mot de passe n'est lu ni enregistré.
3. **Paramètres › Intégrations › Notifications (badges)** : active le connecteur. Facultatif : *Services suivis* (`gmail, discord`) pour n'en suivre que
   quelques-uns.

Tant qu'un service n'est pas connecté, ou que la page affichée n'est pas la sienne (page de connexion), son bouton n'a pas de badge.

## Comment c'est lu

Une page WebView2 **cachée** par service (jamais de fenêtre, son coupé, aucune autorisation, aucun téléchargement) reste ouverte. Le nombre est lu
**sur événement** : à chaque changement du titre de la page (« (3) Discord | #général ») ou fin de navigation ; aucun relevé périodique. Deck réduit ou
masqué, les pages sont mises en veille. Coût : un processus de navigateur (≈ 100 Mo) par service suivi, seulement pour ceux qui ont un bouton.

| Service | Titre lu | Remarque |
|---|---|---|
| Gmail | `(12)` n'importe où dans le titre | |
| Google Chat, Discord, Teams | `(3)` en début de titre | |
| Slack | `(3)` en début de titre, ou `*` / `!` (au moins un) | |

**Ces formats ne sont pas garantis** : les services changent leur page. Non vérifié ici (pas de compte) : le titre réel de chacun ; les tests couvrent les formats
ci-dessus avec de vraies pages WebView2 servies localement. Si un service ne suit plus ce format, son badge reste simplement absent : décris-le alors
toi-même (ci-dessous).

## Décrire son propre service : `notifications.json`

Fichier dans le dossier de configuration (`%APPDATA%\StreamDak`), relu au démarrage :

```json
{
  "providers": [
    {
      "id": "intranet",
      "name": "Intranet",
      "url": "https://intranet.exemple.fr/messages",
      "hosts": ["intranet.exemple.fr"],
      "titleRegex": "Messages \\((\\d+)\\)",
      "selector": "span.badge-unread",
      "attribute": "data-count"
    }
  ]
}
```

| Clé | Rôle |
|---|---|
| `id` | 1 à 32 caractères : minuscules, chiffres, tiret (pas ceux des services livrés) |
| `url` | page suivie (http ou https) |
| `hosts` | noms d'hôte du service (par défaut : celui de `url`) ; un hôte `exemple.fr` couvre `app.exemple.fr` |
| `titleRegex` | expression régulière du titre ; le premier groupe est le nombre |
| `selector` | sélecteur CSS des pastilles de la page : somme de leurs nombres (un élément sans nombre compte pour 1) ; relu toutes les minutes |
| `attribute` | attribut qui porte le nombre (sinon le texte de l'élément) |

Il faut `titleRegex` ou `selector`. Au plus 10 services, 64 Ko. Une entrée invalide est ignorée et signalée dans `streamdak.log`. Le sélecteur est
transmis à la page comme une **chaîne** (jamais collé dans du code), le script ne fait que lire le DOM, et une expression régulière trop lente est
interrompue après 100 ms.

## Limites

- Le badge ne compte que ce que voit la **session de StreamDak**. Si tu lis ta messagerie dans un autre navigateur avec un autre compte, les nombres peuvent différer.
- Sans WebView2 (Windows 11 l'inclut), le connecteur ne peut pas suivre les pages ; il le consigne et n'affiche rien.
- Commande *Actualiser les badges* : relit tout de suite chaque service.
