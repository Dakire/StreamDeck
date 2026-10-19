using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TouchDeck.ViewModels;

/// <summary>Ce que l'éditeur d'étape demande au panneau de propriétés qui le contient.</summary>
public interface IStepOwner
{
    void StepChanged(ActionStepViewModel step);
    void MoveStep(ActionStepViewModel step, int delta);
    void RemoveStep(ActionStepViewModel step);
    IReadOnlyList<string> PageNames { get; }
    IReadOnlyList<string> ProfileNames { get; }
    DeckConfig Config { get; }
    DeckProfile? Profile { get; }
}

public sealed record IssueViewModel(Severity Severity, string Message)
{
    /// <summary>Le niveau est écrit en toutes lettres : la couleur ne porte jamais seule l'information.</summary>
    public string Prefix => Severity == Severity.Error ? "Erreur : " : "Attention : ";
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
        (ActionTypes.Http, "Requête web"), (ActionTypes.Audio, "Audio"), (ActionTypes.Timer, "Minuteur"),
    };

    static readonly (string Key, string Label)[] AudioOptions =
    {
        (AudioCommands.MicToggle, "Micro : couper / rétablir"), (AudioCommands.MicMute, "Couper le micro"), (AudioCommands.MicUnmute, "Rétablir le micro"),
        (AudioCommands.OutToggle, "Son : couper / rétablir"), (AudioCommands.Volume, "Régler le volume"),
    };

    static readonly (string Key, string Label)[] MediaKeys =
    {
        ("playpause", "Lecture / Pause"), ("prev", "Précédent"), ("next", "Suivant"), ("stop", "Stop"),
        ("mute", "Muet"), ("voldown", "Volume −"), ("volup", "Volume +"),
    };

    const string ProgramFilter = "Programmes|*.exe;*.lnk;*.bat;*.cmd|Tous les fichiers|*.*";
    const string ScriptFilter = "Scripts|*.ps1;*.bat;*.cmd|Tous les fichiers|*.*";

    readonly IStepOwner _owner;
    readonly List<string> _pageKeys = new();
    readonly List<string> _profileKeys = new();
    bool _loading = true;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowValue), nameof(ShowMedia), nameof(ShowAudio), nameof(ShowPage), nameof(ShowProfile), nameof(ShowProcesses),
        nameof(ShowBrowseValue), nameof(ShowBrowseArgs), nameof(ShowArgs), nameof(ValueCaption), nameof(ValueHint),
        nameof(ShowValueHint), nameof(ArgsCaption), nameof(ArgsHint), nameof(ShowArgsHint), nameof(TypeLabel))]
    string _type;
    [ObservableProperty] string _value;
    [ObservableProperty] string _args;
    [ObservableProperty] string _title = "";
    [ObservableProperty] bool _canMoveUp;
    [ObservableProperty] bool _canMoveDown;
    [ObservableProperty] bool _canRemove;

    public ObservableCollection<ChoiceViewModel> TypeChoices { get; } = new();
    public ObservableCollection<ChoiceViewModel> MediaChoices { get; } = new();
    public ObservableCollection<ChoiceViewModel> AudioChoices { get; } = new();
    public ObservableCollection<ChoiceViewModel> PageChoices { get; } = new();
    public ObservableCollection<ChoiceViewModel> ProfileChoices { get; } = new();
    public ObservableCollection<ChoiceViewModel> ProcessChoices { get; } = new();
    public ObservableCollection<IssueViewModel> Issues { get; } = new();

    public bool HasIssues => Issues.Count > 0;
    public string TypeLabel => Types.FirstOrDefault(t => t.Key == Type).Label ?? Type;

    public ActionStepViewModel(DeckAction source, IStepOwner owner)
    {
        _owner = owner;
        _type = source.Type;
        _value = source.Value;
        _args = source.Args ?? "";

        foreach (var t in Types) TypeChoices.Add(new ChoiceViewModel(t.Label, () => SetType(t.Key)));
        foreach (var m in MediaKeys) MediaChoices.Add(new ChoiceViewModel(m.Label, () => Value = m.Key));
        foreach (var c in AudioOptions) AudioChoices.Add(new ChoiceViewModel(c.Label, () => Value = c.Key));
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
        RefreshSelection();
        Revalidate();
        _loading = false;
    }

    // ---------- affichage selon le type ----------

    public bool ShowValue => Type is not (ActionTypes.Media or ActionTypes.Audio or ActionTypes.Page or ActionTypes.Profile);
    public bool ShowMedia => Type == ActionTypes.Media;
    public bool ShowAudio => Type == ActionTypes.Audio;
    public bool ShowPage => Type == ActionTypes.Page;
    public bool ShowProfile => Type == ActionTypes.Profile;
    public bool ShowProcesses => Type == ActionTypes.Focus;
    public bool ShowBrowseValue => Type is ActionTypes.Launch or ActionTypes.Script;
    public bool ShowBrowseArgs => Type == ActionTypes.Focus;
    public bool ShowArgs => Type is ActionTypes.Launch or ActionTypes.Script or ActionTypes.Focus or ActionTypes.Http || (Type == ActionTypes.Audio && Value == AudioCommands.Volume);

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

    public string ValueHint => Type switch
    {
        ActionTypes.Hotkey => "Clique dans le champ puis appuie sur ta combinaison (ex. Ctrl+Maj+M, F13). Pour la touche Windows, tape-la : win+d, win+shift+s.",
        ActionTypes.Focus => "Nom du programme (chrome, 3CXDesktopApp…) ou texte présent dans le titre de la fenêtre. Fenêtres ouvertes en ce moment :",
        ActionTypes.Http => "Webhook, domotique, API locale… Sans corps : requête GET ; avec un corps : POST. Délai maximal 10 s.",
        ActionTypes.Timer => "Toucher le bouton lance le compte à rebours, le toucher à nouveau l'arrête. Le temps restant remplace le titre (ou s'insère à la place de {timer}).",
        _ => "",
    };
    public bool ShowValueHint => ValueHint.Length > 0;

    public string ArgsCaption => Type switch
    {
        ActionTypes.Focus => "Si absente, lancer (facultatif)",
        ActionTypes.Script => "Arguments (fichiers uniquement, facultatif)",
        ActionTypes.Http => "Corps de la requête (facultatif, JSON ou texte)",
        ActionTypes.Audio => "Volume (0 à 100)",
        _ => "Arguments (facultatif)",
    };
    public string ArgsHint => Type == ActionTypes.Focus
        ? "Astuce : place cette étape en premier, puis une Pause (≈150 ms), puis un Raccourci : il sera envoyé à cette fenêtre." : "";
    public bool ShowArgsHint => ArgsHint.Length > 0;

    // ---------- état ----------

    void SetType(string key)
    {
        if (Type == key) return;
        _loading = true;
        Type = key;
        Value = key switch { ActionTypes.Delay => "500", ActionTypes.Url or ActionTypes.Http => "https://", ActionTypes.Page => PageTargets.Home, ActionTypes.Audio => AudioCommands.MicToggle, ActionTypes.Timer => "300", _ => "" };
        Args = "";
        _loading = false;
        LoadProcesses();
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
        foreach (var proc in WindowFocus.OpenProcessNames())
        {
            var pn = proc;
            ProcessChoices.Add(new ChoiceViewModel(pn, () => Value = pn));
        }
    }

    void RefreshSelection()
    {
        for (int i = 0; i < Types.Length; i++) TypeChoices[i].IsSelected = Types[i].Key == Type;
        for (int i = 0; i < MediaKeys.Length; i++) MediaChoices[i].IsSelected = MediaKeys[i].Key == Value;
        for (int i = 0; i < AudioOptions.Length; i++) AudioChoices[i].IsSelected = AudioOptions[i].Key == Value;
        for (int i = 0; i < PageChoices.Count; i++)
            PageChoices[i].IsSelected = string.Equals(_pageKeys[i], Value, StringComparison.OrdinalIgnoreCase);
        for (int i = 0; i < ProfileChoices.Count; i++)
            ProfileChoices[i].IsSelected = string.Equals(_profileKeys[i], Value, StringComparison.OrdinalIgnoreCase);
        foreach (var p in ProcessChoices) p.IsSelected = string.Equals(p.Text, Value, StringComparison.OrdinalIgnoreCase);
    }

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
