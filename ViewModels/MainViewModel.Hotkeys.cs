using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using StreamDak.Actions;
using StreamDak.Hotkeys;

namespace StreamDak.ViewModels;

// Raccourcis clavier globaux : un bouton portant GlobalHotkey se déclenche depuis n'importe quelle application.
public partial class MainViewModel
{
    readonly IHotkeyService _hotkeyService;
    readonly Dictionary<int, DeckButton> _hotkeyButtons = new();
    string _hotkeySignature = "";

    /// <summary>Raccourcis globaux refusés (syntaxe, doublon, déjà pris par une autre application) avec la raison ; affichés dans l'inspecteur.</summary>
    public IReadOnlyList<RejectedHotkey> RejectedHotkeys { get; private set; } = Array.Empty<RejectedHotkey>();

    /// <summary>Raison du refus du raccourci global de ce bouton, ou null s'il est actif (ou absent).</summary>
    public string? HotkeyProblem(DeckButton button) =>
        RejectedHotkeys.FirstOrDefault(r => string.Equals(r.Combo, button.GlobalHotkey, StringComparison.OrdinalIgnoreCase) && r.Label == (string.IsNullOrWhiteSpace(button.Label) ? "(sans titre)" : button.Label))?.Reason;

    /// <summary>Met les enregistrements du système en accord avec la configuration (seulement si les raccourcis ont changé).</summary>
    void SyncHotkeys()
    {
        var (planned, rejected) = GlobalHotkeyPlan.Build(_doc.Config);
        var signature = string.Join("|", planned.Select(p => p.Id + "=" + p.Combo + "@" + p.Button.GetHashCode()).Concat(rejected.Select(r => "!" + r.Combo)));
        if (signature == _hotkeySignature) return;
        _hotkeySignature = signature;

        foreach (var id in _hotkeyButtons.Keys.ToList()) _hotkeyService.Unregister(id);
        _hotkeyButtons.Clear();
        var refused = new List<RejectedHotkey>(rejected);
        foreach (var p in planned)
        {
            var result = _hotkeyService.Register(p.Id, p.Combo);
            if (result.Ok) _hotkeyButtons[p.Id] = p.Button;
            else refused.Add(new RejectedHotkey(p.Button.GlobalHotkey!, string.IsNullOrWhiteSpace(p.Button.Label) ? "(sans titre)" : p.Button.Label, result.Error ?? "Refusé par Windows"));
        }
        RejectedHotkeys = refused;
        foreach (var r in refused) Log.Warn($"Raccourci global « {r.Combo} » ({r.Label}) refusé : {r.Reason}");
        if (refused.Count > 0) Notifier.Warning($"{refused.Count} raccourci(s) global(aux) refusé(s) : voir l'inspecteur du bouton");
        if (_hotkeyButtons.Count > 0) Log.Info($"Raccourcis globaux : {_hotkeyButtons.Count} enregistré(s)");
    }

    void OnHotkeyPressed(int id)
    {
        // Levé sur le thread des raccourcis : le bouton s'exécute sur celui de l'interface
        _ui.BeginInvoke(DispatcherPriority.Input, () => RunHotkeyButton(id));
    }

    void RunHotkeyButton(int id)
    {
        if (!_hotkeyButtons.TryGetValue(id, out var button) || IsEdit) return;   // en édition, un raccourci ne déclenche rien
        if (Tiles.OfType<DeckTileViewModel>().FirstOrDefault(t => ReferenceEquals(t.Button, button)) is { } tile)
        {
            _ = RunTileAsync(tile);
            return;
        }
        // Bouton d'une autre page ou d'un autre profil : exécuté sans tuile (pas d'anneau ni de coche, mais l'échec est annoncé)
        _ = RunDetachedAsync(button);
    }

    async System.Threading.Tasks.Task RunDetachedAsync(DeckButton button)
    {
        var result = await _engine.RunAsync(button, this);
        if (!result.Ok && result.Outcome != ActionOutcome.Cancelled)
        {
            Log.Warn($"Raccourci global « {button.GlobalHotkey} » ({button.Label}) : {result.Message}");
            SetStatus($"{button.Label} : {result.Message}");
        }
    }
}
