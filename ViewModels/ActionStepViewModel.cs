using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDak.Integrations;

namespace StreamDak.ViewModels;

/// <summary>Ce que l'éditeur d'étape demande au panneau de propriétés qui le contient.</summary>
public interface IStepOwner
{
    /// <summary>L'étape a changé : le propriétaire l'enregistre dans le bouton (annulable).</summary>
    void StepChanged(ActionStepViewModel step);
    /// <summary>Déplace l'étape de <paramref name="delta"/> rangs.</summary>
    void MoveStep(ActionStepViewModel step, int delta);
    /// <summary>Supprime l'étape.</summary>
    void RemoveStep(ActionStepViewModel step);
    /// <summary>Pages du profil, proposées pour l'action « page ».</summary>
    IReadOnlyList<string> PageNames { get; }
    /// <summary>Profils, proposés pour l'action « profile ».</summary>
    IReadOnlyList<string> ProfileNames { get; }
    /// <summary>Intégrations disponibles : chacune ajoute un type d'action avec ses commandes.</summary>
    IReadOnlyList<IIntegration> Integrations { get; }
    /// <summary>Programmes qui ont une fenêtre ouverte, proposés pour l'action « fenêtre au premier plan ».</summary>
    IReadOnlyList<string> OpenPrograms { get; }
    /// <summary>Configuration, pour la validation.</summary>
    DeckConfig Config { get; }
    /// <summary>Profil du bouton, pour vérifier les noms de page (null : non vérifiés).</summary>
    DeckProfile? Profile { get; }
}

/// <summary>Problème affiché sous une étape.</summary>
/// <param name="Severity">Gravité.</param>
/// <param name="Message">Explication.</param>
public sealed record IssueViewModel(Severity Severity, string Message)
{
    /// <summary>Le niveau est écrit en toutes lettres : la couleur ne porte jamais seule l'information.</summary>
    public string Prefix => Severity == Severity.Error ? "Erreur : " : "Attention : ";
    /// <summary>Icône selon la gravité.</summary>
    public Wpf.Ui.Controls.SymbolRegular Symbol => Severity == Severity.Error
        ? Wpf.Ui.Controls.SymbolRegular.ErrorCircle24 : Wpf.Ui.Controls.SymbolRegular.Warning24;
}

/// <summary>Une étape d'un bouton : type, valeur et arguments, avec les choix propres à chaque type et la validation en direct.</summary>
public partial class ActionStepViewModel : ObservableObject
{
    /// <summary>Destinations spéciales de l'action « page », proposées avant les pages du profil.</summary>
    static readonly string[] SpecialPages = { PageTargets.Home, PageTargets.Back };

    static readonly (string Key, string Label)[] Types =
    {
        (ActionTypes.Hotkey, "Raccourci"), (ActionTypes.Text, "Texte"), (ActionTypes.Media, "Média"),
        (ActionTypes.Launch, "Programme"), (ActionTypes.Script, "Script"), (ActionTypes.Url, "Lien web"),
        (ActionTypes.Page, "Page"), (ActionTypes.Profile, "Profil"), (ActionTypes.Focus, "Fenêtre"), (ActionTypes.Delay, "Pause"),
        (ActionTypes.Http, "Requête web"), (ActionTypes.Audio, "Audio"), (ActionTypes.AudioDevice, "Audio : périphérique"), (ActionTypes.Timer, "Minuteur"),
    };

    static readonly (string Key, string Label)[] AudioOptions =
    {
        (AudioCommands.MicToggle, "Micro : couper / rétablir"), (AudioCommands.MicMute, "Couper le micro"), (AudioCommands.MicUnmute, "Rétablir le micro"),
        (AudioCommands.OutToggle, "Son : couper / rétablir"), (AudioCommands.Volume, "Régler le volume"),
    };

    static readonly (string Key, string Label)[] AudioDeviceOptions =
    {
        (AudioDeviceCommands.Toggle, "Couper / rétablir"), (AudioDeviceCommands.Mute, "Couper"), (AudioDeviceCommands.Unmute, "Rétablir"),
        (AudioDeviceCommands.Volume, "Régler le niveau"), (AudioDeviceCommands.List, "Lister les périphériques"),
    };

    static readonly (string Key, string Label)[] MediaKeys =
    {
        ("playpause", "Lecture / Pause"), ("prev", "Précédent"), ("next", "Suivant"), ("stop", "Stop"),
        ("mute", "Muet"), ("voldown", "Volume −"), ("volup", "Volume +"),
    };

    const string ProgramFilter = "Programmes|*.exe;*.lnk;*.bat;*.cmd|Tous les fichiers|*.*";
    const string ScriptFilter = "Scripts|*.ps1;*.bat;*.cmd|Tous les fichiers|*.*";

    readonly IStepOwner _owner;
    readonly List<(string Key, string Label)> _types = new(Types);
    readonly List<string> _commandKeys = new();
    readonly List<string> _pageKeys = new();
    readonly List<string> _profileKeys = new();
    bool _loading = true;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowValue), nameof(ShowCommand), nameof(ShowMedia), nameof(ShowAudio), nameof(ShowAudioDevice), nameof(ShowPage), nameof(ShowProfile), nameof(ShowProcesses),
        nameof(ShowBrowseValue), nameof(ShowBrowseArgs), nameof(ShowArgs), nameof(ValueCaption), nameof(ValueHint),
        nameof(ShowValueHint), nameof(ArgsCaption), nameof(ArgsHint), nameof(ShowArgsHint), nameof(TypeLabel))]
    string _type;
    [ObservableProperty] string _value;
    [ObservableProperty] string _args;
    [ObservableProperty] string _title = "";
    [ObservableProperty] bool _canMoveUp;
    [ObservableProperty] bool _canMoveDown;
    [ObservableProperty] bool _canRemove;

    /// <summary>Types d'action proposés.</summary>
    public ObservableCollection<ChoiceViewModel> TypeChoices { get; } = new();
    /// <summary>Touches média proposées.</summary>
    public ObservableCollection<ChoiceViewModel> MediaChoices { get; } = new();
    /// <summary>Commandes de l'intégration choisie (OBS, vMix…).</summary>
    public ObservableCollection<ChoiceViewModel> CommandChoices { get; } = new();
    /// <summary>Commandes audio proposées.</summary>
    public ObservableCollection<ChoiceViewModel> AudioChoices { get; } = new();
    /// <summary>Commandes proposées pour un périphérique audio choisi par son nom.</summary>
    public ObservableCollection<ChoiceViewModel> AudioDeviceChoices { get; } = new();
    /// <summary>Pages proposées (accueil, retour, puis pages du profil).</summary>
    public ObservableCollection<ChoiceViewModel> PageChoices { get; } = new();
    /// <summary>Profils proposés.</summary>
    public ObservableCollection<ChoiceViewModel> ProfileChoices { get; } = new();
    /// <summary>Programmes ouverts, proposés pour la fenêtre au premier plan.</summary>
    public ObservableCollection<ChoiceViewModel> ProcessChoices { get; } = new();
    /// <summary>Problèmes de l'étape.</summary>
    public ObservableCollection<IssueViewModel> Issues { get; } = new();

    /// <summary>true si l'étape a au moins un problème.</summary>
    public bool HasIssues => Issues.Count > 0;
    /// <summary>Nom du type affiché (« Raccourci », « Requête web »…).</summary>
    public string TypeLabel => _types.FirstOrDefault(t => t.Key == Type).Label ?? Type;

    /// <summary>Étape éditable, copie de <paramref name="source"/> ; les changements remontent à <paramref name="owner"/>.</summary>
    public ActionStepViewModel(DeckAction source, IStepOwner owner)
    {
        _owner = owner;
        _type = source.Type;
        _value = source.Value;
        _args = source.Args ?? "";

        foreach (var integration in owner.Integrations) _types.Add((integration.Id, integration.Name));
        foreach (var t in _types) TypeChoices.Add(new ChoiceViewModel(t.Label, () => SetType(t.Key)));
        foreach (var m in MediaKeys) MediaChoices.Add(new ChoiceViewModel(m.Label, () => Value = m.Key));
        foreach (var c in AudioOptions) AudioChoices.Add(new ChoiceViewModel(c.Label, () => Value = c.Key));
        foreach (var c in AudioDeviceOptions) AudioDeviceChoices.Add(new ChoiceViewModel(c.Label, () => Value = c.Key));
        foreach (var name in SpecialPages.Concat(owner.PageNames))
        {
            var n = name;
            PageChoices.Add(new ChoiceViewModel(n == PageTargets.Home ? "Accueil" : n == PageTargets.Back ? "Retour" : n, () => Value = n));
            _pageKeys.Add(n);
        }
        foreach (var name in owner.ProfileNames)
        {
            var n = name;
            ProfileChoices.Add(new ChoiceViewModel(n, () => Value = n));
            _profileKeys.Add(n);
        }
        LoadProcesses();
        LoadCommands();
        RefreshSelection();
        Revalidate();
        _loading = false;
    }

    // ---------- affichage selon le type ----------

    /// <summary>true si la valeur se saisit au clavier (sinon elle se choisit dans une liste).</summary>
    public bool ShowValue => !IsIntegration && Type is not (ActionTypes.Media or ActionTypes.Audio or ActionTypes.AudioDevice or ActionTypes.Page or ActionTypes.Profile);
    /// <summary>true pour choisir une commande d'intégration.</summary>
    public bool ShowCommand => IsIntegration;
    bool IsIntegration => Integration != null;
    IIntegration? Integration => _owner.Integrations.FirstOrDefault(i => i.Id == Type);
    IntegrationCommand? Command => Integration?.Commands.FirstOrDefault(c => c.Name.Equals(Value, StringComparison.OrdinalIgnoreCase));
    /// <summary>true pour choisir une touche média.</summary>
    public bool ShowMedia => Type == ActionTypes.Media;
    /// <summary>true pour choisir une commande audio.</summary>
    public bool ShowAudio => Type == ActionTypes.Audio;
    /// <summary>true pour choisir une commande sur un périphérique audio désigné par son nom.</summary>
    public bool ShowAudioDevice => Type == ActionTypes.AudioDevice;
    /// <summary>true pour choisir une page.</summary>
    public bool ShowPage => Type == ActionTypes.Page;
    /// <summary>true pour choisir un profil.</summary>
    public bool ShowProfile => Type == ActionTypes.Profile;
    /// <summary>true pour proposer les programmes ouverts (fenêtre au premier plan).</summary>
    public bool ShowProcesses => Type == ActionTypes.Focus;
    /// <summary>true pour proposer « Parcourir » sur la valeur (programme, script).</summary>
    public bool ShowBrowseValue => Type is ActionTypes.Launch or ActionTypes.Script;
    /// <summary>true pour proposer « Parcourir » sur les arguments (programme à lancer si la fenêtre manque).</summary>
    public bool ShowBrowseArgs => Type == ActionTypes.Focus;
    /// <summary>true si le type utilise les arguments (le volume n'en a que pour la commande « volume »).</summary>
    public bool ShowArgs => IsIntegration || Type is ActionTypes.Launch or ActionTypes.Script or ActionTypes.Focus or ActionTypes.Http || (Type == ActionTypes.Audio && Value == AudioCommands.Volume) || (Type == ActionTypes.AudioDevice && Value != AudioDeviceCommands.List);

    /// <summary>Libellé du champ de valeur selon le type.</summary>
    public string ValueCaption => Type switch
    {
        ActionTypes.Hotkey => "Combinaison",
        ActionTypes.Text => "Texte à saisir",
        ActionTypes.Launch => "Programme ou dossier",
        ActionTypes.Script => "Commande PowerShell ou fichier .ps1 / .bat / .cmd",
        ActionTypes.Url => "Adresse",
        ActionTypes.Focus => "Programme ou titre de la fenêtre",
        ActionTypes.Delay => "Durée (millisecondes)",
        ActionTypes.Http => "Adresse à appeler (https://…)",
        ActionTypes.Timer => "Durée (secondes)",
        _ => "",
    };

    /// <summary>Aide affichée sous le champ de valeur, selon le type.</summary>
    public string ValueHint => IsIntegration ? (Command?.Description ?? "") + (Integration!.State == ConnectionState.Connected ? "" : $"  ({Integration.Name} n'est pas connecté : réglages dans Paramètres › Intégrations.)") : Type switch
    {
        ActionTypes.Hotkey => "Clique dans le champ puis appuie sur ta combinaison (ex. Ctrl+Maj+M, F13). Pour la touche Windows, tape-la : win+d, win+shift+s.",
        ActionTypes.Focus => "Nom du programme (chrome, 3CXDesktopApp…) ou texte présent dans le titre de la fenêtre. Fenêtres ouvertes en ce moment :",
        ActionTypes.Http => "Webhook, domotique, API locale… Sans corps : requête GET ; avec un corps : POST. Délai maximal 10 s.",
        ActionTypes.Timer => "Toucher le bouton lance le compte à rebours, le toucher à nouveau l'arrête. Le temps restant remplace le titre (ou s'insère à la place de {timer}).",
        _ => "",
    };
    /// <summary>true si une aide est disponible pour la valeur.</summary>
    public bool ShowValueHint => ValueHint.Length > 0;

    /// <summary>Libellé du champ d'arguments selon le type.</summary>
    public string ArgsCaption => IsIntegration ? (Command?.ArgHint is { } hint ? Capitalize(hint) + (Command.ArgRequired ? "" : " (facultatif)") : "Argument (facultatif)") : Type switch
    {
        ActionTypes.Focus => "Si absente, lancer (facultatif)",
        ActionTypes.Script => "Arguments (fichiers uniquement, facultatif)",
        ActionTypes.Http => "Corps de la requête (facultatif, JSON ou texte)",
        ActionTypes.Audio => "Volume (0 à 100)",
        ActionTypes.AudioDevice => Value == AudioDeviceCommands.Volume ? "Périphérique=niveau (ex. Focusrite=60, Arctis=+5)" : "Nom du périphérique (ex. Focusrite, capture:Focusrite)",
        _ => "Arguments (facultatif)",
    };
    /// <summary>Conseil d'enchaînement pour la fenêtre au premier plan.</summary>
    public string ArgsHint => Type == ActionTypes.Focus
        ? "Astuce : place cette étape en premier, puis une Pause (≈150 ms), puis un Raccourci : il sera envoyé à cette fenêtre."
        : Type == ActionTypes.AudioDevice
            ? "Une partie du nom suffit, sans tenir compte des majuscules ; « capture: » (micro) ou « render: » (sortie) lève un doute. Le périphérique par défaut de Windows n'est pas changé. « Lister les périphériques » affiche les noms."
            : "";
    /// <summary>true si un conseil est disponible pour les arguments.</summary>
    public bool ShowArgsHint => ArgsHint.Length > 0;

    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], System.Globalization.CultureInfo.CurrentCulture) + s[1..];

    // ---------- état ----------

    void SetType(string key)
    {
        if (Type == key) return;
        _loading = true;
        Type = key;
        Value = _owner.Integrations.FirstOrDefault(i => i.Id == key) is { } integration ? (integration.Commands.Count > 0 ? integration.Commands[0].Name : "")
            : key switch { ActionTypes.Delay => "500", ActionTypes.Url or ActionTypes.Http => "https://", ActionTypes.Page => PageTargets.Home, ActionTypes.Audio => AudioCommands.MicToggle, ActionTypes.AudioDevice => AudioDeviceCommands.Toggle, ActionTypes.Timer => "300", _ => "" };
        Args = "";
        _loading = false;
        LoadProcesses();
        LoadCommands();
        RefreshSelection();
        Changed();
    }

    partial void OnValueChanged(string value)
    {
        if (Type is ActionTypes.Delay or ActionTypes.Timer && value.Any(c => !char.IsDigit(c)))
        {
            Value = new string(value.Where(char.IsDigit).ToArray());   // la durée n'accepte que des chiffres
            return;
        }
        OnPropertyChanged(nameof(ShowArgs));   // « volume » est la seule commande audio avec un niveau
        OnPropertyChanged(nameof(ArgsCaption));
        RefreshSelection();
        Changed();
    }

    partial void OnArgsChanged(string value) => Changed();

    void Changed()
    {
        if (_loading) return;
        Revalidate();
        _owner.StepChanged(this);
    }

    /// <summary>Recalcule les problèmes de l'étape (validation en direct).</summary>
    public void Revalidate()
    {
        Issues.Clear();
        foreach (var i in ActionValidator.Validate(ToDeckAction(), _owner.Config, _owner.Profile))
            Issues.Add(new IssueViewModel(i.Severity, i.Message));
        OnPropertyChanged(nameof(HasIssues));
    }

    void LoadProcesses()
    {
        ProcessChoices.Clear();
        if (Type != ActionTypes.Focus) return;
        foreach (var proc in _owner.OpenPrograms)
        {
            var pn = proc;
            ProcessChoices.Add(new ChoiceViewModel(pn, () => Value = pn));
        }
    }

    void LoadCommands()
    {
        CommandChoices.Clear();
        _commandKeys.Clear();
        if (Integration is not { } integration) return;
        foreach (var command in integration.Commands)
        {
            var name = command.Name;
            CommandChoices.Add(new ChoiceViewModel(command.Label, () => Value = name));
            _commandKeys.Add(name);
        }
    }

    void RefreshSelection()
    {
        for (int i = 0; i < _types.Count; i++) TypeChoices[i].IsSelected = _types[i].Key == Type;
        for (int i = 0; i < CommandChoices.Count; i++) CommandChoices[i].IsSelected = string.Equals(_commandKeys[i], Value, StringComparison.OrdinalIgnoreCase);
        for (int i = 0; i < MediaKeys.Length; i++) MediaChoices[i].IsSelected = MediaKeys[i].Key == Value;
        for (int i = 0; i < AudioOptions.Length; i++) AudioChoices[i].IsSelected = AudioOptions[i].Key == Value;
        for (int i = 0; i < AudioDeviceOptions.Length; i++) AudioDeviceChoices[i].IsSelected = AudioDeviceOptions[i].Key == Value;
        for (int i = 0; i < PageChoices.Count; i++)
            PageChoices[i].IsSelected = string.Equals(_pageKeys[i], Value, StringComparison.OrdinalIgnoreCase);
        for (int i = 0; i < ProfileChoices.Count; i++)
            ProfileChoices[i].IsSelected = string.Equals(_profileKeys[i], Value, StringComparison.OrdinalIgnoreCase);
        foreach (var p in ProcessChoices) p.IsSelected = string.Equals(p.Text, Value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Étape telle qu'elle sera enregistrée (arguments vides omis).</summary>
    public DeckAction ToDeckAction() => new()
    {
        Type = Type, Value = Value, Args = string.IsNullOrEmpty(Args) ? null : Args,
    };

    // ---------- commandes ----------

    [RelayCommand] void MoveUp() => _owner.MoveStep(this, -1);
    [RelayCommand] void MoveDown() => _owner.MoveStep(this, 1);
    [RelayCommand] void Remove() => _owner.RemoveStep(this);

    [RelayCommand]
    void BrowseValue()
    {
        var path = PickFile(Type == ActionTypes.Script ? ScriptFilter : ProgramFilter);
        if (path != null) Value = path;
    }

    [RelayCommand]
    void BrowseArgs()
    {
        var path = PickFile(ProgramFilter);
        if (path != null) Args = path;
    }

    static string? PickFile(string filter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
