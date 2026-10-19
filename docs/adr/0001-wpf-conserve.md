# 0001 — Garder WPF

- **Statut** : acceptée.
- **Date** : 2026.

## Contexte

La refonte visuelle demandait un rendu moderne : Fluent, Mica, coins arrondis, animations. Avalonia et WinUI 3 ont été
envisagés.

Le cœur du produit est une fenêtre qui ne prend jamais le focus (`WS_EX_NOACTIVATE` et filtre `WM_MOUSEACTIVATE`) : sans
elle, les raccourcis envoyés partiraient vers le deck au lieu de l'application active.

## Décision

Garder WPF (.NET 9) avec WPF-UI (licence MIT) pour les contrôles Fluent et Mica.

| Critère | WPF | Avalonia | WinUI 3 |
|---|---|---|---|
| Fenêtre sans focus | Maîtrisée, testée | Par HWND natif, à refaire | Possible, plus contrainte |
| Linux | Non | Oui (X11 ; Wayland partiel) | Non |
| Qualité visuelle | Suffisante (démontrée par la refonte) | Équivalente | Équivalente |
| Coût de migration | Nul | Réécriture des vues, styles et harnais de tests | Réécriture et empaquetage |

## Conséquences

- Windows uniquement. Un logiciel réservé à macOS n'est pas intégrable en natif (voir `integrations-research.md`).
- La logique (`Core/`) ne dépend ni de WPF ni de Win32 : une interface Avalonia pourrait s'y brancher plus tard.
- Mica exige Windows 11 ; Windows 10 reçoit un fond uni.
