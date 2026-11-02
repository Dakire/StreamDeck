# 0029 — WebView2 : aucun pont page → application dans les cadres visibles

- **Statut** : acceptée.
- **Date** : 2 novembre 2026.

## Contexte

Le relevé du champ qui a le focus dans un cadre visible (`_fieldTimer`, toutes les 400 ms, `ExecuteScriptAsync`) a un coût. La piste « script injecté à la création du document + `WebMessageReceived` »
l'éliminerait, mais elle exige `IsWebMessageEnabled = true` sur le cadre visible, donc `chrome.webview.postMessage` pour tout script du site affiché : un pont page → application (SECURITY.md, ADR 0017).

## Décision

- **Le pont n'est pas créé.** Les cadres visibles gardent `IsWebMessageEnabled = false` et `AreHostObjectsAllowed = false`.
- Le coût est réduit autrement : minuteur coupé quand le deck est masqué ou réduit, `ExecuteScriptAsync` borné à 2 s (une page figée ne garde plus le relevé « en cours »), travail seulement en interaction.
- `MemoryUsageTargetLevel = Low` sur les cadres rangés, les sessions cachées et, en mode économie, les cadres affichés.
- Les cadres rangés ne sont **pas suspendus** (`TrySuspendAsync`) : leurs notifications doivent continuer d'arriver (ADR 0018).

## Précision de la documentation

Les **sessions cachées** de badges ont `IsWebMessageEnabled = true` quand un observateur est installé (ADR 0022) : le message est validé par `NotificationMessage.TryRead` (type, hôte, entier borné) et la page n'a
accès à rien d'autre. SECURITY.md l'affirmait « absolu » ; il distingue maintenant les deux cas. L'environnement WebView2 est **unique** (un seul processus navigateur, des processus de rendu par site),
et non « un processus par cadre » comme le disaient les ADR 0018 et 0020.
