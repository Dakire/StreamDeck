using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StreamDak.Actions;
using StreamDak.Audio;

namespace StreamDak.ViewModels;

// Réglages des widgets « Niveau audio » (canal Sonar, Discord ou périphérique Windows, et forme) et « Mélangeur audio » (canaux affichés),
// et niveau du bouton de réinitialisation, commun aux deux
public sealed partial class PropertiesViewModel
{
    const string WindowsPrefix = "windows:";

    /// <summary>Cibles proposées : un canal Sonar par pastille, Discord (micro, sortie), puis « Périphérique Windows ».</summary>
    public ObservableCollection<ChoiceViewModel> LevelTargetChoices { get; } = new();
    /// <summary>Formes proposées.</summary>
    public ObservableCollection<ChoiceViewModel> LevelShapeChoices { get; } = new();
    /// <summary>Périphériques audio actifs de Windows, proposés pour remplir le nom.</summary>
    public ObservableCollection<ChoiceViewModel> LevelDeviceChoices { get; } = new();
    /// <summary>Canaux du mélangeur : un toucher ajoute ou retire le canal (Sonar, Discord, périphériques Windows détectés).</summary>
    public ObservableCollection<ChoiceViewModel> MixerChoices { get; } = new();

    [ObservableProperty] string _levelDevice = "";
    [ObservableProperty] bool _showLevelDevice;
    [ObservableProperty] string _levelDeviceMessage = "";
    /// <summary>Niveau du bouton de réinitialisation saisi (vide : celui de la cible).</summary>
    [ObservableProperty] string _levelReset = "";
    [ObservableProperty] string _levelResetError = "";

    IAudioDevices? _audioDevices;
    (Place? Place, int Index) _levelModeFor;
    bool _levelWindowsMode;
    readonly List<LevelTarget?> _levelTargets = new();   // cible de chaque pastille de LevelTargetChoices ; null : « Périphérique Windows »
    readonly List<LevelTarget> _mixerTargets = new();     // cible de chaque pastille de MixerChoices

    /// <summary>true pour le widget niveau audio.</summary>
    public bool IsLevelWidget => _widgetId == WidgetTypes.Level;
    /// <summary>true pour le widget mélangeur audio.</summary>
    public bool IsMixerWidget => _widgetId == WidgetTypes.Mixer;
    /// <summary>true pour l'un des deux widgets audio (niveau de réinitialisation).</summary>
    public bool IsAudioWidget => IsLevelWidget || IsMixerWidget;

    static IEnumerable<(LevelTarget Target, string Label)> FixedTargets() =>
        LevelOptions.SonarChannels.Select(c => (new LevelTarget(LevelBackend.Sonar, c.Channel), "Sonar : " + c.Label))
            .Concat(LevelOptions.DiscordTargets.Select(d => (new LevelTarget(LevelBackend.Discord, d.Target), "Discord : " + d.Label)));

    void InitLevelChoices(IAudioDevices? audio)
    {
        _audioDevices = audio;
        foreach (var (target, label) in FixedTargets())
        {
            var t = target;
            _levelTargets.Add(t);
            LevelTargetChoices.Add(new ChoiceViewModel(label, () =>
            {
                _levelWindowsMode = false;
                ShowLevelDevice = false;
                ApplyNow(b => b.SetOption(LevelOptions.Source, t.ToText()), t.Backend == LevelBackend.Discord ? "Volume Discord" : "Canal Sonar");
            }));
        }
        _levelTargets.Add(null);
        LevelTargetChoices.Add(new ChoiceViewModel("Périphérique Windows", () =>
        {
            _levelWindowsMode = true;
            ShowLevelDevice = true;
            RefreshLevelDevices();
            ApplyNow(b => b.SetOption(LevelOptions.Source, LevelDevice.Trim().Length > 0 ? WindowsPrefix + LevelDevice.Trim() : null), "Périphérique audio");
            RefreshLevelSelection(_target);
        }));
        foreach (var (shape, label) in LevelShapes.All)
        {
            var s = shape;
            LevelShapeChoices.Add(new ChoiceViewModel(label, () => ApplyNow(b => b.SetOption(LevelOptions.Shape, LevelShapes.KeyOf(s)), "Forme du contrôle")));
        }
    }

    /// <summary>Charge les réglages du niveau audio ou du mélangeur depuis le bouton ; appelé pendant le rechargement.</summary>
    void LoadLevel(DeckButton? b)
    {
        OnPropertyChanged(nameof(IsLevelWidget));
        OnPropertyChanged(nameof(IsMixerWidget));
        OnPropertyChanged(nameof(IsAudioWidget));
        LevelReset = b?.Option(LevelOptions.Reset) ?? "";
        LevelResetError = "";
        if (b != null && _widgetId == WidgetTypes.Mixer) { ShowLevelDevice = false; RefreshMixerChoices(b); return; }
        if (b == null || _widgetId != WidgetTypes.Level) { ShowLevelDevice = false; return; }
        if (_levelModeFor != (_place, _index)) { _levelModeFor = (_place, _index); _levelWindowsMode = false; }
        if (LevelTarget.TryParse(b.Option(LevelOptions.Source), out var target))
        {
            _levelWindowsMode = target.Backend == LevelBackend.Windows;
            LevelDevice = target.Backend == LevelBackend.Windows ? target.Name : "";
        }
        else if (!_levelWindowsMode) LevelDevice = "";
        ShowLevelDevice = _levelWindowsMode;
        if (_levelWindowsMode) RefreshLevelDevices();
        RefreshLevelSelection(b);
    }

    void RefreshLevelSelection(DeckButton? b)
    {
        _ = LevelTarget.TryParse(b?.Option(LevelOptions.Source), out var target);
        for (int i = 0; i < _levelTargets.Count; i++)
            LevelTargetChoices[i].IsSelected = _levelTargets[i] is { } t
                ? !_levelWindowsMode && target != null && string.Equals(target.ToText(), t.ToText(), StringComparison.OrdinalIgnoreCase)
                : _levelWindowsMode;
        var shape = LevelShapes.Parse(b?.Option(LevelOptions.Shape));
        for (int i = 0; i < LevelShapes.All.Count; i++) LevelShapeChoices[i].IsSelected = LevelShapes.All[i].Shape == shape;
    }

    /// <summary>Pastilles du mélangeur : Sonar et Discord, puis les périphériques Windows détectés ; celles du mélangeur sont cochées.</summary>
    void RefreshMixerChoices(DeckButton b)
    {
        var chosen = LevelOptions.MixerTargets(b.Option(LevelOptions.Sources)).Select(t => t.ToText()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = FixedTargets().ToList();
        try
        {
            if (_audioDevices != null)
                targets.AddRange(_audioDevices.Endpoints().Select(e => (new LevelTarget(LevelBackend.Windows, (e.Flow == AudioFlow.Capture ? "capture:" : "render:") + e.Name),
                    (e.Flow == AudioFlow.Capture ? "Entrée : " : "Sortie : ") + e.Name)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* liste indisponible : Sonar et Discord restent proposés */ }
        // Un périphérique du mélangeur débranché reste affiché (coché) pour pouvoir le retirer
        foreach (var text in chosen.Where(c => targets.All(t => !t.Target.ToText().Equals(c, StringComparison.OrdinalIgnoreCase))))
            if (LevelTarget.TryParse(text, out var missing)) targets.Add((missing, missing.Name + " (absent)"));

        MixerChoices.Clear();
        _mixerTargets.Clear();
        foreach (var (target, label) in targets)
        {
            var t = target;
            _mixerTargets.Add(t);
            MixerChoices.Add(new ChoiceViewModel(label, () => ToggleMixerTarget(t)) { IsSelected = chosen.Contains(t.ToText()) });
        }
    }

    void ToggleMixerTarget(LevelTarget target)
    {
        ApplyNow(b =>
        {
            var current = LevelOptions.MixerTargets(b.Option(LevelOptions.Sources)).Select(t => t.ToText()).ToList();
            var text = target.ToText();
            int at = current.FindIndex(c => c.Equals(text, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) { if (current.Count > 1) current.RemoveAt(at); }   // au moins un canal
            else if (current.Count < 8) current.Add(text);
            var joined = string.Join(",", current);
            b.SetOption(LevelOptions.Sources, joined.Equals(LevelOptions.DefaultMixer, StringComparison.OrdinalIgnoreCase) ? null : joined);
        }, "Canaux du mélangeur");
    }

    partial void OnLevelResetChanged(string value)
    {
        if (_loading || !IsAudioWidget) return;
        var text = value.Trim().TrimEnd('%').Trim();
        if (text.Length > 0 && !(int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) && n is >= 0 and <= 100))
        {
            LevelResetError = "Un nombre de 0 à 100";
            DropDraft("level-reset");
            return;
        }
        LevelResetError = "";
        Draft("level-reset", "Niveau de réinitialisation", b => b.SetOption(LevelOptions.Reset, text.Length > 0 ? text : null));
    }

    /// <summary>Périphériques audio actifs, en pastilles : un toucher remplit le nom (nom complet, avec le sens pour lever tout doute).</summary>
    void RefreshLevelDevices()
    {
        LevelDeviceChoices.Clear();
        LevelDeviceMessage = "";
        if (_audioDevices == null) return;
        try
        {
            foreach (var endpoint in _audioDevices.Endpoints())
            {
                var text = (endpoint.Flow == AudioFlow.Capture ? "capture:" : "render:") + endpoint.Name;
                LevelDeviceChoices.Add(new ChoiceViewModel((endpoint.Flow == AudioFlow.Capture ? "Entrée : " : "Sortie : ") + endpoint.Name, () => { LevelDevice = text; Flush(); }));
            }
            if (LevelDeviceChoices.Count == 0) LevelDeviceMessage = "Aucun périphérique audio actif.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            LevelDeviceMessage = "Liste des périphériques indisponible.";
        }
    }

    partial void OnLevelDeviceChanged(string value)
    {
        if (_loading || !_levelWindowsMode) return;
        var text = value.Trim();
        if (text.Length > 0)
        {
            try { AudioDeviceQuery.Parse(text); }
            catch (ArgumentException) { DropDraft("level-device"); return; }
        }
        Draft("level-device", "Périphérique audio", b => b.SetOption(LevelOptions.Source, text.Length > 0 ? WindowsPrefix + text : null));
    }
}
