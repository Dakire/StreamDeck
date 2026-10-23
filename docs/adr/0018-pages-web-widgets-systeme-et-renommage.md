# 0018 — Pages web plein cadre, widgets Processeur et Mémoire, renommage en StreamDak

- **Statut** : acceptée.
- **Date** : 23 octobre 2026.

## Contexte

Quatre demandes liées : renommer le produit (TouchDeck → StreamDak), un nouveau logo, une page entière affichant un site (au lieu d'une grille
de boutons) et des widgets de charge du système. Une cinquième concernait la connexion à Google dans le cadre web.
La pile est WPF / .NET 10 : il n'y a ni serveur Node, ni Electron, ni iframe. « Backend » et « frontend » sont dans le même processus.

## Décisions

### Renommage

Tout le code, les projets, les espaces de noms et les textes passent à **StreamDak** (casse respectée : `TouchDeck` → `StreamDak`,
`touchdeck` → `streamdak`, `TOUCHDECK` → `STREAMDAK`). Ce qui est **conservé exprès**, parce que des données de l'utilisateur en dépendent :

- **Entropie DPAPI** des secrets (`TouchDeck.secrets.v1`, `DpapiSecretStore`) : elle fait partie du chiffrement. La renommer rendrait
  illisibles les mots de passe et jetons déjà enregistrés. Le test `SecretsEncryptedBeforeTheRenameStillDecrypt` le garde.
- **Ancien dossier de configuration** `%APPDATA%\TouchDeck` : `ConfigStore.MigrateLegacyDirectory`, appelé au démarrage par `App`, le
  **déplace** vers `%APPDATA%\StreamDak` si ce dernier n'existe pas (configuration, secrets, plugins, images, profil WebView2). S'il est tenu
  par une ancienne instance, l'ancien dossier reste utilisé. Le déplacement n'a jamais lieu à l'initialisation statique : les tests ne touchent
  donc pas aux vraies données. Sans effet quand le dossier est imposé par variable d'environnement.
- **Variable d'environnement** : `STREAMDAK_DATA_DIR`, avec repli sur l'ancienne `TOUCHDECK_DATA_DIR`.
- **Fichiers de profil** : extension `.streamdak`, mais les `.touchdeck` existants restent proposés par les boîtes de dialogue et lus (la
  lecture se fait sur le contenu, pas sur l'extension).
- **Démarrage avec Windows** : la valeur de registre `StreamDak` remplace `TouchDeck`, qui est lue (état activé) et supprimée à la prochaine
  écriture, pour ne pas lancer l'application deux fois.
- **Plugins** compilés contre `TouchDeck.Core` : à recompiler contre `StreamDak.Core` (le nom de l'assembly change ; aucun moyen de l'éviter).

### Logo

Dessin vectoriel unique (tuile en dégradé, 3 × 3 touches allumées en serpentin comme un flux qui se termine par une touche blanche) :
`Assets/logo.svg` (référence), `Themes/Logo.xaml` (`DrawingImage` utilisé par la barre du deck et la fenêtre des paramètres : net à toutes
les tailles), `Assets/logo.png` et `Assets/icon.ico` (barre des tâches, zone de notification) générés depuis le même dessin.

### Page web plein cadre

`DeckPage.Kind` (absent = grille ; `"web"`) et `DeckPage.Url`, deux clés facultatives, absentes d'une page ordinaire (`JsonIgnore`) ; les
anciens fichiers se relisent tels quels. `ConfigNormalizer` ramène un type inconnu à la grille (avec avertissement). La `Version` du format ne
change pas.

Le deck n'affiche alors **aucune grille** : une seule tuile, le widget web de l'ADR 0017, posée à la case 0 et sur toute la grille
(`TileGridPanel` ramène l'emprise aux colonnes et lignes disponibles). Tout ce qui garde la page chargée (cadre WebView2 isolé, masquage
sous les menus, saisie au clavier) est donc partagé avec le widget. Les boutons de la page sont **conservés** : repasser la page en grille les
retrouve. Édition : bouton *Page* du dock › *Afficher une page web…* / *Adresse de la page web…* / *Afficher une grille de boutons* ; la
saisie « calendar.google.com » est complétée en `https://` (`WebAddress.TryParse`), tout autre schéma est refusé.

### Connexion à Google dans le cadre : mesure, pas contournement

Le symptôme rapporté (« pas de bouton *Se connecter* ») a été mesuré avec un profil WebView2 vierge (WebView2 155), 12 essais sur 4
configurations (agent utilisateur par défaut, agent « Chrome » sans `Edg/`, avec et sans `--disable-blink-features=AutomationControlled`) :

- `calendar.google.com` → `accounts.google.com/ServiceLogin` → `workspace.google.com/…/products/calendar/` : **identique dans toutes les
  configurations**. C'est la page d'accueil que Google affiche à un visiteur non connecté, pas un refus ;
- le lien *Sign in* de cette page existe dans toutes les configurations et mène à une vraie page de connexion (« Sign in to continue to Google
  Calendar / Email or phone / Next »), là aussi identique. Dans une petite tuile, la page d'accueil replie son menu : le lien y est caché.
  Une **page entière** (ci-dessus) règle ce cas.

**Cause réelle du bouton *Sign in* sans effet** (constatée sur une capture de l'utilisateur, après la mesure ci-dessus) : ce bouton ouvre un
nouvel onglet, et le cadre refusait toute nouvelle fenêtre sans rien charger. Correctif : un lien « nouvel onglet » (`target="_blank"`,
`window.open`) s'ouvre dans le cadre lui-même, toujours sans fenêtre et seulement en http(s) (test `ALinkThatOpensANewTabLoadsInsideTheFrame`,
qui échoue sans le correctif). La connexion qui suit reste soumise aux règles de Google, voir ci-dessous.

**Décision : l'agent utilisateur n'est pas modifié.** Il n'a aucun effet mesurable sur ces pages, et usurper l'identité d'un navigateur pour
passer une protection de Google contre les navigateurs intégrés n'est ni souhaitable ni honnête. L'étape suivante (envoi d'un identifiant)
n'a pas été automatisée. À la place :

- si Google rejette le cadre (page `signin/rejected` ou erreur `disallowed_useragent`), le widget le dit en clair et propose les deux issues
  propres (`EmbeddedBrowserRefusal`) : le widget **Agenda**, qui passe par l'API Google avec une autorisation donnée dans le navigateur
  système (ADR 0017), ou le nouveau bouton **Ouvrir dans le navigateur** de la barre du cadre (adresse configurée seulement) ;
- **non vérifié ici** : la connexion complète d'un vrai compte dans le cadre (identifiant, mot de passe, double authentification), et les
  adresses exactes de la page de refus de Google (reconnues d'après leur forme connue, jamais observées).

### Saisie au clavier dans le cadre

La fenêtre du deck ne prend jamais le focus (ADR 0013) : taper dans une page demandait de toucher d'abord le bouton clavier, que l'on ne
remarquait pas (journal : aucun passage en saisie après l'ouverture de la page de connexion Google). Le cadre interroge maintenant la page
toutes les 400 ms avec un script en lecture seule exécuté *par l'application* (l'élément ayant le focus accepte-t-il du texte ? iframes de même
origine comprises) : la saisie démarre dès qu'un champ de texte est actif, et s'arrête ~1,2 s après en être sorti, sauf si elle a été lancée
avec le bouton clavier. Ce n'est pas un pont page → application (la page n'appelle rien et n'envoie rien). Limite : un champ dans une iframe
d'une autre origine n'est pas vu ; le bouton clavier reste là pour ce cas.

### Notifications, micro et caméra (décision de l'utilisateur, remplace une règle de l'ADR 0017)

L'ADR 0017 refusait toute autorisation. L'utilisateur veut les notifications (rappels de Google Agenda), le micro et la caméra (Meet, Chat). Ils sont
accordés **seulement** aux pages en `https` (ou à la machine locale) du **même site** que l'adresse saisie pour le cadre (`WebPermissions.MayUseDevices` :
`meet.google.com` pour une page `calendar.google.com`, mais ni `evil.com` vers lequel un lien aurait mené, ni un autre site sous un même `co.uk`).
Tout autre droit (position, presse-papiers, capteurs…) reste refusé ; chaque demande est journalisée (type et nom d'hôte, jamais l'adresse complète).
Windows doit aussi autoriser le micro et la caméra aux applications de bureau (Paramètres › Confidentialité). Non vérifié ici : une vraie
notification Windows, un vrai micro ou une vraie caméra (pas de matériel ni de compte dans les tests ; la décision d'accorder ou de refuser est testée
avec un vrai WebView2 sur `Notification.requestPermission`).

### Cadres conservés d'une page du deck à l'autre

Changer de page détruisait le cadre : la page web se rechargeait à chaque retour. Le cadre est maintenant **rangé** (`WebFramePool`) quand sa tuile quitte
l'écran et repris tel quel au retour : pas de rechargement, la connexion, le défilement et la saisie en cours sont gardés, et les notifications continuent
d'arriver. Clé = profil + page + adresse (page web) ou emplacement + adresse (widget) ; changer l'adresse donne un nouveau cadre. Coût : un processus de
navigateur par cadre rangé, 6 au plus (le plus ancien est libéré) ; tout est libéré à la fermeture du deck. Les gestionnaires d'événements du cadre passent
par la tuile qui l'affiche (`WebFrame.Vm`), jamais par une vue qui n'existe plus.

### Zoom

Boutons − / pourcentage (revient à 100 %) / + dans la barre du cadre, paliers de navigateur (25 % à 500 %), pincement du doigt et Ctrl + molette repris
(événement du contrôle, plus un relevé toutes les 400 ms). Enregistré : `DeckPage.Zoom` pour une page web, option `zoom` du widget web ; absent à 100 %.
L'enregistrement est un réglage (hors historique) qui ne recrée pas le cadre.

### Barre du haut compatible Windows 11 (accrochage)

La fenêtre était `NoResize` : Windows ne l'accrochait pas et n'affichait pas les dispositions d'accrochage. Maintenant :

- `ResizeMode="CanResize"` (cadre redimensionnable, boutons agrandir présents au sens de Windows ; taille minimale 640 × 420) : accrochage par glissement
  vers un bord ou le haut d'un écran, Win + flèches. `WS_EX_NOACTIVATE` et le filtre `WM_MOUSEACTIVATE` sont **inchangés** (un test le garde).
- Le bouton plein écran est annoncé à Windows comme **bouton agrandir** (`WM_NCHITTEST` → `HTMAXBUTTON`) : la disposition d'accrochage s'affiche au survol
  de la souris. Jamais au doigt (le doigt n'a pas de survol, et un appui non client ne passerait pas par le traitement tactile du deck, ADR 0013) : pendant
  800 ms après un appui, la barre garde son fonctionnement tactile. Pas de bouton agrandir sous un menu ou un dialogue. Le clic est traité au relâchement
  (`WM_NCLBUTTONUP`) et lance le plein écran du deck ; la couche de survol du bouton est allumée à la main (WPF ne voit plus la souris dans la zone non cliente).
- **Un seul plein écran** : l'agrandissement demandé par le système (`SC_MAXIMIZE` : disposition d'accrochage, Win + Haut) passe par le plein écran du deck
  (zone de travail exacte en pixels physiques, multi-écran), pas par l'état « agrandie » de WPF. L'état plein écran et l'icône suivent la taille réelle
  (`SyncFullFlag`) : accrocher à une moitié ou redimensionner à la main sort du plein écran. Si WPF se retrouve quand même agrandie (accrochage natif du haut),
  l'icône suit et le bouton restaure.
- En plein écran, les bords **ne redimensionnent pas** (`HTCLIENT`) : le toucher en bordure d'écran va aux touches.
- Glisser la barre (`DragMove`, boucle de déplacement du système) : Windows 11 y ajoute l'accrochage et ses dispositions en haut d'écran. Au cas où le
  déplacement natif ne le ferait pas pour une fenêtre sans focus, la fenêtre lâchée à la souris sur le bord haut d'un écran passe en plein écran sur cet écran
  (rien au doigt).
- **Non vérifié ici** : l'affichage réel de la disposition d'accrochage au survol, et le glissement vers un bord (boucle de déplacement du système, pas de
  bureau piloté). Vérifié : styles `WS_THICKFRAME` / `WS_MAXIMIZEBOX` / `WS_EX_NOACTIVATE`, réponses `WM_NCHITTEST` réelles (bouton, reste de la barre, bords en plein
  écran, toucher récent, menu ouvert), survol.

### Widgets Processeur et Mémoire

Pas de point de terminaison ni de WebSocket : tout est dans le processus. L'intégration « Système Windows » (`sys`) relevait déjà la charge
du processeur et la mémoire (%) chaque seconde ; elle publie en plus `ram.used` et `ram.total` (Go, point décimal). Le widget lit les
valeurs comme les titres dynamiques et l'agenda (`ValuesChanged`), donc se rafraîchit à chaque relevé (1 s).

- Mémoire : le relevé `GC.GetGCMemoryInfo().MemoryLoadBytes` n'est mis à jour qu'à la fin d'un ramasse-miettes (valeur périmée sur un
  processus calme) ; la sonde lit maintenant le compteur « Memory / Available Bytes » à chaque relevé.
- Un widget affiché sur un deck dont l'intégration est éteinte l'**active lui-même** (réglage `Integrations.sys.Enabled`, hors historique,
  une fois) : elle n'a ni compte, ni réseau, ni secret. Sans relevé, la tuile dit pourquoi au lieu d'afficher un zéro.
- Alerte : la jauge passe en couleur de danger à partir de 85 %.
- Nouvelles valeurs de `Widget` : `cpu` et `ram`. Aucune nouvelle clé de `config.json`.

## Conséquences

- `config.json` : deux clés facultatives de page (`Kind`, `Url`), deux identifiants de widget. Compatible dans les deux sens pour les pages
  ordinaires ; un fichier avec une page web ouvert par une ancienne version la verrait comme une grille (clés ignorées).
- Une page web coûte un processus de navigateur tant qu'elle est affichée (comme le widget).
- Le cadre est une fenêtre Win32 : même limites que l'ADR 0017 (rien ne le recouvre, le toucher va au navigateur).
