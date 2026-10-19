using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TouchDeck.ViewModels;

/// <summary>Édition d'une action d'un bouton : type, valeur et arguments, avec les choix propres à chaque type.</summary>
public partial class ActionEditorViewModel : ObservableObject
{
    static readonly (string Key, string Label, string Icon)[] Types =
    {
        ("hotkey", "Raccourci", "⌨️"), ("text", "Texte", "🔤"), ("media", "Média", "🎵"),
        ("launch", "Programme", "🚀"), ("script", "Script", "⚡"), ("url", "Lien web", "🌐"),
        ("page", "Page", "📄"), ("focus", "Fenêtre", "🪟"), ("delay", "Pause", "⏱️"),
    };

    static readonly (string Key, string Label)[] MediaKeys =
    {
        ("playpause", "Lecture / Pause"), ("prev", "Précédent"), ("next", "Suivant"), ("stop", "Stop"),
        ("mute", "Muet"), ("voldown", "Volume −"), ("volup", "Volume +"),
    };

    const string ProgramFilter = "Programmes|*.exe;*.lnk;*.bat;*.cmd|Tous les fichiers|*.*";
    const string ScriptFilter = "Scripts|*.ps1;*.bat;*.cmd|Tous les fichiers|*.*";

    readonly ButtonEditorViewModel _owner;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowValue), nameof(ShowMedia), nameof(ShowPage), nameof(ShowProcesses),
        nameof(ShowBrowseValue), nameof(ShowBrowseArgs), nameof(ShowArgs), nameof(ValueCaption), nameof(ValueHint),
        nameof(ShowValueHint), nameof(ArgsCaption), nameof(ArgsHint), nameof(ShowArgsHint))]
    string _type;
    [ObservableProperty] string _value;
    [ObservableProperty] string _args;
    [ObservableProperty] string _title = "";
    [ObservableProperty] bool _canMoveUp;
    [ObservableProperty] bool _canMoveDown;
    [ObservableProperty] bool _canRemove;

    public ObservableCollection<ChoiceViewModel> TypeChoices { get; } = new();
    public ObservableCollection<ChoiceViewModel> MediaChoices { get; } = new();
    public ObservableCollection<ChoiceViewModel> PageChoices { get; } = new();
    public ObservableCollection<ChoiceViewModel> ProcessChoices { get; } = new();

    public ActionEditorViewModel(DeckAction source, ButtonEditorViewModel owner, string[] pageNames)
    {
        _owner = owner;
        _type = source.Type;
        _value = source.Value;
        _args = source.Args ?? "";

        foreach (var t in Types) TypeChoices.Add(new ChoiceViewModel(t.Label, () => SetType(t.Key)));
        foreach (var m in MediaKeys) MediaChoices.Add(new ChoiceViewModel(m.Label, () => Value = m.Key));
        foreach (var name in new[] { "home", "back" }.Concat(pageNames))
        {
            var n = name;
            string text = n == "home" ? "Accueil" : n == "back" ? "Retour" : n;
            PageChoices.Add(new ChoiceViewModel(text, () => Value = n) { });
            _pageKeys.Add(n);
        }
        LoadProcesses();
        RefreshSelection();
    }

    readonly System.Collections.Generic.List<string> _pageKeys = new();

    // ---------- affichage selon le type ----------

    public bool ShowValue => Type is not ("media" or "page");
    public bool ShowMedia => Type == "media";
    public bool ShowPage => Type == "page";
    public bool ShowProcesses => Type == "focus";
    public bool ShowBrowseValue => Type is "launch" or "script";
    public bool ShowBrowseArgs => Type == "focus";
    public bool ShowArgs => Type is "launch" or "script" or "focus";

    public string ValueCaption => Type switch
    {
        "hotkey" => "COMBINAISON",
        "text" => "TEXTE À SAISIR",
        "launch" => "PROGRAMME OU DOSSIER",
        "script" => "COMMANDE POWERSHELL OU FICHIER .ps1 / .bat / .cmd",
        "url" => "ADRESSE",
        "focus" => "PROGRAMME OU TITRE DE LA FENÊTRE",
        "delay" => "DURÉE (MILLISECONDES)",
        _ => "",
    };

    public string ValueHint => Type switch
    {
        "hotkey" => "Clique dans le champ puis appuie sur ta combinaison (ex. Ctrl+Maj+M, F13). Pour la touche Windows, tape-la : win+d, win+shift+s.",
        "focus" => "Nom du programme (chrome, 3CXDesktopApp…) ou texte présent dans le titre de la fenêtre. Fenêtres ouvertes en ce moment :",
        _ => "",
    };
    public bool ShowValueHint => ValueHint.Length > 0;

    public string ArgsCaption => Type == "focus" ? "SI ABSENTE, LANCER (FACULTATIF)"
        : Type == "script" ? "ARGUMENTS (FICHIERS UNIQUEMENT, FACULTATIF)" : "ARGUMENTS (FACULTATIF)";
    public string ArgsHint => Type == "focus"
        ? "Astuce : place cette action en premier, puis une Pause (≈150 ms), puis un Raccourci : il sera envoyé à cette fenêtre." : "";
    public bool ShowArgsHint => ArgsHint.Length > 0;

    // ---------- état ----------

    void SetType(string key)
    {
        if (Type == key) return;
        Type = key;
        Value = key == "delay" ? "500" : "";
        Args = "";
        LoadProcesses();
        RefreshSelection();
    }

    partial void OnValueChanged(string value)
    {
        if (Type == "delay" && value.Any(c => !char.IsDigit(c)))
        {
            Value = new string(value.Where(char.IsDigit).ToArray());   // la durée n'accepte que des chiffres
            return;
        }
        RefreshSelection();
    }

    void LoadProcesses()
    {
        ProcessChoices.Clear();
        if (Type != "focus") return;
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
        for (int i = 0; i < PageChoices.Count; i++)
            PageChoices[i].IsSelected = string.Equals(_pageKeys[i], Value, StringComparison.OrdinalIgnoreCase);
        foreach (var p in ProcessChoices) p.IsSelected = string.Equals(p.Text, Value, StringComparison.OrdinalIgnoreCase);
    }

    public DeckAction ToDeckAction() => new()
    {
        Type = Type, Value = Value, Args = string.IsNullOrEmpty(Args) ? null : Args,
    };

    // ---------- commandes ----------

    [RelayCommand] void MoveUp() => _owner.MoveAction(this, -1);
    [RelayCommand] void MoveDown() => _owner.MoveAction(this, 1);
    [RelayCommand] void Remove() => _owner.RemoveAction(this);

    [RelayCommand]
    void BrowseValue()
    {
        var path = PickFile(Type == "script" ? ScriptFilter : ProgramFilter);
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
