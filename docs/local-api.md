# API locale

TouchDeck écoute sur un **canal nommé Windows** (`\.\pipe\TouchDeck.Api.<utilisateur>.<clé>`), réservé à l'utilisateur courant
(`PipeOptions.CurrentUserOnly`) : aucun port réseau n'est ouvert. La clé de 8 caractères dérive du dossier de configuration,
pour que les essais avec `TOUCHDECK_DATA_DIR` n'interfèrent pas avec ta vraie configuration.

Protocole : une requête JSON par ligne, une réponse JSON par ligne, 64 Ko au plus par requête, 8 clients simultanés.
Toute réponse contient `"ok": true` ou `"ok": false` avec `"error"` (message lisible).

| Requête | Effet |
|---|---|
| `{"cmd":"ping"}` | Vérifie que TouchDeck répond (`protocol` : 1) |
| `{"cmd":"status"}` | Profil actif, profils et pages, état de chaque intégration |
| `{"cmd":"values"}` | Valeurs des intégrations (`obs.scene`, `sys.cpu`…) |
| `{"cmd":"profile","name":"Streaming"}` | Affiche un profil |
| `{"cmd":"page","name":"Outils"}` | Affiche une page du profil courant (`@home`, `@back` aussi) |
| `{"cmd":"run","action":{"type":"hotkey","value":"ctrl+c"}}` | Exécute une action (même validation et même moteur que les boutons) |
| `{"cmd":"run","actions":[…]}` | Exécute jusqu'à 20 actions à la suite |
| `{"cmd":"show"}` / `{"cmd":"edit"}` / `{"cmd":"settings"}` | Affiche le deck, l'éditeur, les paramètres |

## Exemple PowerShell

```powershell
$name = "TouchDeck.Api.$env:USERNAME.<clé>"   # visible dans touchdeck.log : « API locale à l'écoute sur le canal … »
$c = [System.IO.Pipes.NamedPipeClientStream]::new(".", $name, "InOut")
$c.Connect(3000)
$w = [System.IO.StreamWriter]::new($c); $w.AutoFlush = $true
$r = [System.IO.StreamReader]::new($c)
$w.WriteLine('{"cmd":"run","action":{"type":"url","value":"https://example.com"}}')
$r.ReadLine()
```

## Sécurité

- Seuls les processus de **ton compte** se connectent. Ils peuvent faire ce que le deck fait (y compris `launch` et `script`),
  mais ils pourraient déjà lancer ces programmes eux-mêmes ; l'API n'élargit pas les droits.
- Ne jamais exposer ce canal sur le réseau (redirection, tunnel) : ce serait une exécution de commandes à distance.
- Une action invalide (touche inconnue, type inconnu) est refusée avant exécution.

## Seconde instance

Lancer `TouchDeck.exe` quand l'application tourne déjà (même dossier de configuration) n'ouvre pas de seconde fenêtre :
la nouvelle instance envoie `show` (ou `edit` avec `--edit`) à la première et se ferme (≈ 200 ms).
