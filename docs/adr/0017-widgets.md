# 0017 — Widgets : horloge, agenda Google, page web

- **Statut** : acceptée.
- **Date** : 22 octobre 2026.

## Contexte

Le deck n'affichait que des touches : on ne pouvait ni voir l'heure en grand, ni l'agenda, ni une page web (chat, tableau de bord).
Demande : des widgets (heure, agenda, chat « iframe »), dans l'esprit d'un deck Elgato ou Corsair, sans casser le principe d'une fenêtre
qui ne prend jamais le focus (ADR 0013, 0015) et sans exécuter de code distant dans l'application (AGENTS.md).

## Décision

**Modèle (ajout compatible de `config.json`, validé avec l'utilisateur).** Un widget est un `DeckButton` qui porte `Widget`
(« clock », « agenda », « web »), `WidgetOptions` (réglages par clé), `ColSpan` et `RowSpan` (emprise en cases). Ces quatre clés sont absentes
d'un bouton ordinaire (`JsonIgnore`), les anciens fichiers se relisent tels quels, `ConfigNormalizer` répare un widget inconnu ou une emprise
absurde. La `Version` du format ne change pas.

**Grille.** `TileGridPanel` remplace `UniformGrid` : chaque tuile est posée à son numéro de case avec son emprise. Les cases qu'un widget
recouvre n'ont pas de tuile et doivent être vides dans le document. Si un voisin est occupé, l'emprise est **réduite** (`TileSpans.Effective`) :
un bouton n'est jamais caché. Le panneau de propriétés refuse un agrandissement qui n'a pas la place, avec un message.
En mode édition un widget est une touche comme les autres (sélectionnable, avec son emprise) ; hors édition c'est une tuile d'affichage.

**Horloge.** Pure (`WidgetClock`), rafraîchie par la minuterie du deck (déjà là pour les titres dynamiques).

**Agenda.** L'intégration Google (`GoogleIntegration`, OAuth « application de bureau » avec le projet Google Cloud de l'utilisateur) lit
l'agenda principal toutes les 5 minutes (`events.list`, 14 jours, droit `calendar.events` déjà demandé) et publie `calendar.events` (JSON compact)
ou `calendar.error` dans ses valeurs d'état. Le widget s'abonne aux valeurs de l'intégration, comme les titres dynamiques. Un échec de lecture
n'interrompt pas les autres commandes Google. `CalendarFeed` et `AgendaFormatter` (Core) sont purs et testés.

**Page web.** Le cadre est un WebView2 (`Microsoft.Web.WebView2`), exception **explicitement acceptée** à la règle « pas de code distant » :
la page s'affiche dans un navigateur isolé, elle n'est pas exécutée par TouchDeck. Garde-fous (`WebWidgetView`) :

- profil de navigation propre à TouchDeck (`<dossier de configuration>\WebView2`), jamais celui d'Edge ;
- aucun objet hôte exposé, aucun message page → application, outils de développement et menus contextuels coupés ;
- ni fenêtre, ni téléchargement, ni autorisation (caméra, micro, position, notifications) ;
- navigation limitée à `http` et `https` ; adresse saisie par l'utilisateur seulement, validée (`WidgetTypes.IsWebAddress`) avant d'être écrite.

**Contraintes du cadre.** C'est une fenêtre Win32 enfant :

- rien de WPF ne peut la recouvrir : un menu ou une boîte de dialogue la masque (`Suspended`), sans recharger la page. Un message
  d'état (bandeau) posé sur la ligne du bas peut rester caché derrière elle ;
- le toucher dans la page va directement au navigateur, pas au hook du deck : un appui long sur la page n'ouvre donc pas le menu de la case
  (modifier le widget se fait en mode édition) ;
- le clavier reste à l'application active, comme pour tout le deck. Le bouton *Saisie au clavier* de la tuile rend la fenêtre activable comme
  en mode édition (`SetEditing`), met le focus dans la page, puis rend le focus à l'application précédente quand on le touche de nouveau.

## Conséquences

- Nouvelle dépendance : `Microsoft.Web.WebView2` (composant Edge WebView2, préinstallé sur Windows 11 ; sans lui la tuile le dit).
- Un widget web coûte un processus de navigateur tant que sa page est affichée ; le cadre est libéré quand la page change.
- Limite connue : « ajouter dans la première case libre » peut viser une case recouverte par un widget ; le widget réduit alors son emprise au lieu de cacher le bouton.
- Non vérifié ici (pas d'écran tactile ni de session Google) : le comportement du cadre WebView2 au doigt et sans focus, et l'agenda contre un vrai compte.
