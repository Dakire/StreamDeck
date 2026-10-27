using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StreamDak.Actions;
using StreamDak.Audio;

namespace StreamDak.ViewModels;

// Réglages du widget « Niveau audio » : canal Sonar ou périphérique Windows, et forme (roue, curseur vertical, curseur horizontal)
public sealed partial class PropertiesViewModel
{
    const string WindowsPrefix = "windows:";

    /// <summary>Cibles proposées : un canal Sonar par pastille, puis « Périphérique Windows ».</summary>
    public ObservableCollection<ChoiceViewModel> LevelTargetChoices { get; } = new();
    /// <summary>Formes proposées.</summary>
    public ObservableCollection<ChoiceViewModel> LevelShapeChoices { get; } = new();
    /// <summary>Périphériques audio actifs de Windows, proposés pour remplir le nom.</summary>
    public ObservableCollection<ChoiceViewModel> LevelDeviceChoices { get; } = new();

    [ObservableProperty] string _levelDevice = "";
    [ObservableProperty] bool _showLevelDevice;
    [ObservableProperty] string _levelDeviceMessage = "";

    IAudioDevices? _audioDevices;
    (Place? Place, int Index) _levelModeFor;
    bool _levelWindowsMode;

    /// <summary>true pour le widget niveau audio.</summary>
    public bool IsLevelWidget => _widgetId == WidgetTypes.Level;

    void InitLevelChoices(IAudioDevices? audio)
    {
        _audioDevices = audio;
        foreach (var (channel, label) in LevelOptions.SonarChannels)
        {
            var ch = channel;
            LevelTargetChoices.Add(new ChoiceViewModel("Sonar : " + label, () =>
            {
                _levelWindowsMode = false;
                ApplyNow(b => b.SetOption(LevelOptions.Source, new LevelTarget(LevelBackend.Sonar, ch).ToText()), "Canal Sonar");
            }));
        }
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

    /// <summary>Charge les réglages du niveau audio depuis le bouton ; appelé pendant le rechargement.</summary>
    void LoadLevel(DeckButton? b)
    {
        OnPropertyChanged(nameof(IsLevelWidget));
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
        for (int i = 0; i < LevelOptions.SonarChannels.Count; i++)
            LevelTargetChoices[i].IsSelected = !_levelWindowsMode && target is { Backend: LevelBackend.Sonar } && target.Name.Equals(LevelOptions.SonarChannels[i].Channel, StringComparison.OrdinalIgnoreCase);
        LevelTargetChoices[^1].IsSelected = _levelWindowsMode;
        var shape = LevelShapes.Parse(b?.Option(LevelOptions.Shape));
        for (int i = 0; i < LevelShapes.All.Count; i++) LevelShapeChoices[i].IsSelected = LevelShapes.All[i].Shape == shape;
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
