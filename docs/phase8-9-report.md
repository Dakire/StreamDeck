# Bilan des phases 8 et 9 : intégrations, plugins, API locale

20 octobre 2026, directement sur `main` (validé), après la phase 3 partielle. Chaque étape est un commit compilé sans
avertissement, avec ses tests. **354 tests au vert** (232 en fin de phase 2).

## En bref

- **Cadre d'intégrations** (`IIntegration`, `IntegrationManager`, secrets DPAPI) et **onze connecteurs** : OBS, vMix, lampes Elgato,
  Philips Hue, Système Windows (processeur, mémoire, média en cours donc Spotify), Microsoft Teams, X-Plane 12, Twitch, ClickUp,
  Notion, monday.com. Chacun a sa page `docs/integrations/` et ses tests contre un faux serveur.
- **35 boutons de raccourcis** prêts à l'emploi (Zoom, Teams, Discord, Premiere, Photoshop, DaVinci, Blender, Outlook, Word, Excel,
  Star Citizen) pour les logiciels sans API locale.
- **5 modèles de profils**, **icône de la zone de notification**, **instance unique**, **API locale** (canal nommé), **plugins locaux**
  avec un exemple compilé et chargé pour de bon (`samples/HelloPlugin`).
- Clé `Integrations` ajoutée à `config.json` (validée) ; les anciens fichiers restent lisibles.

## Ce que tu peux regarder

| Où | Quoi |
|---|---|
| Paramètres › Intégrations | une carte par connecteur : activer, réglages, état, **Tester la connexion** ; **Associer** pour Hue et Teams |
| Éditeur › catalogue | une catégorie par intégration, plus « Applications cibles » |
| Éditeur › clic droit sur un profil | **Nouveau profil depuis un modèle** |
| Titre d'un bouton | `{obs.scene:hors ligne}`, `CPU {sys.cpu} %`, `{sys.media.title}` ; voyant plein / vide sur les commandes à état |
| Zone de notification | afficher le deck, éditeur, paramètres, quitter |
| `streamdak.log` | connexions, plugins chargés (empreinte SHA-256), canal de l'API locale |

## Comment tester

```powershell
dotnet test tests/StreamDak.Tests            # 354 tests, aucun vrai logiciel nécessaire
$env:STREAMDAK_DATA_DIR = "$env:TEMP\td-essai"; dotnet run -c Release -- --simulate   # essai sans risque
```

Plugin d'exemple : `dotnet build samples/HelloPlugin -c Release`, copier `HelloPlugin.dll` et `HelloPlugin.deps.json` dans
`%STREAMDAK_DATA_DIR%\plugins\HelloPlugin\`, relancer, puis envoyer à l'API locale
`{"cmd":"run","action":{"type":"hello","value":"greet","args":"Guillaume"}}` (voir `docs/local-api.md`).

Vérifié sur l'application réelle (mode `--simulate`, dossier temporaire) : réponse de l'API locale (`ping`, `status`, `run`),
seconde instance qui se ferme en ≈ 200 ms, plugin d'exemple chargé et visible dans `status`, icône de notification créée sans erreur.

## Mesures

Application réelle, `--simulate`, configuration par défaut (profil « Principal », deux pages), 3 lancements.

| Mesure | Fin de phase 2 | Maintenant |
|---|---|---|
| Fenêtre affichée | 665 à 682 ms | 596 à 606 ms (le premier lancement à froid : 1 291 ms) |
| Services prêts | ≈ 100 ms | 107 à 112 ms |
| Mémoire de travail | 164 à 165 Mo | 167 Mo |
| Mémoire privée | ≈ 130 Mo | 106 Mo |
| CPU au repos (10 s) | 16 ms | 0 ms |

Avec l'intégration **Système Windows** activée (relevé chaque seconde) : +22 Mo de mémoire de travail (WinRT, compteurs de performance)
et ≈ 5 ms de CPU par seconde (0,5 %). Les intégrations désactivées ne coûtent rien au repos. Les comparaisons sont indicatives : la charge de la machine
varie d'une mesure à l'autre et il n'y a qu'une série.

## Écarts avec ta demande, et pourquoi

| Demandé | État | Raison |
|---|---|---|
| Streamlabs Desktop, YouTube, Graph, Google Workspace | Non fait | Protocole peu documenté, ou application à faire vérifier / enregistrer par toi (voir l'étude, section 0) |
| Discord RPC, Spotify Web API, Voicemod | Non fait | Approbation du service, compte Premium, ou clé partenaire : je ne contourne aucune condition. Discord : raccourcis globaux ; Spotify : média en cours par SMTC |
| MSFS (SimConnect), Wave Link, LIFX, Adobe UXP, DaVinci scripting, Blender add-on | Non fait | SDK à redistribuer, protocole non officiel, ou plugin externe : réalisables en **plugin** |
| Appui long / double appui | Non fait | Demande de nouvelles clés dans `config.json` : à valider avec toi |
| Assistant de premier lancement, palette Ctrl+K | Non fait | Les modèles de profils couvrent le départ rapide ; l'éditeur a déjà une recherche instantanée |
| Traduction FR / EN | Non fait | Toute l'interface est en français dans le XAML et le code : une extraction complète est un chantier à part |
| Phase 3 (fichiers Colors / Typography / Effects, sélecteur de couleur), phase 4 (barre latérale, inspecteur), phase 5 (composant DeckKey) | Partiel | Palette, jetons, thème AMOLED faits ; l'éditeur a déjà catalogue / grille / propriétés ; `TileFace` fait office de DeckKey avec animation d'appui |
| Tests contre les vrais Teams, X-Plane, Hue, vMix, Twitch… | Non fait | Je n'ai pas ces logiciels : chaque protocole est joué contre un faux serveur et les points incertains sont marqués « à vérifier » dans les pages |

## Risques

- **Teams et X-Plane** : API que je n'ai pu confronter qu'à ma lecture de leur documentation et à des faux serveurs ; un écart de nom de champ est possible.
- **Plugins** : le code d'un plugin a tes droits. Documenté, empreinte journalisée, jamais téléchargé ; pas de bac à sable.
- **API locale** : réservée à ton compte Windows, mais elle peut lancer des programmes comme n'importe quel bouton. À ne jamais rediriger vers le réseau.
- **Cible `net9.0-windows10.0.19041.0`** pour l'application et les tests (média WinRT) : Windows 10 2004 minimum.
- **Un identifiant de catalogue** en doublon avec une intégration avait cassé un test (ordre d'exécution) : les raccourcis d'applications sont maintenant préfixés `preset.`.

## Décisions à prendre

1. Appui long / double appui sur les boutons (nouvelles clés `config.json`) : oui ou non ?
2. Quelles intégrations « non faites » t'importent vraiment (Streamlabs, Voicemod si tu as une clé, YouTube si tu acceptes la vérification Google) ?
3. Traduction FR / EN : à lancer comme chantier dédié ?
