using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace TouchDeck.ViewModels;

/// <summary>
/// Panneau de création rapide d'un bouton : une zone de recherche d'action (catalogue complet : actions natives, applications cibles,
/// intégrations), un titre et une icône proposés automatiquement, « Créer » et « Annuler ». Tout vit dans un brouillon : le document
/// n'est écrit qu'une fois, à « Créer » (un seul pas d'annulation). Aucune écriture à la frappe.
/// </summary>
public sealed partial class QuickCreateViewModel : ObservableObject
{
    readonly DeckDocument _doc;
    readonly IAppWindows _windows;
    readonly Place _place;
    readonly int _index;
    bool _syncing, _titleEdited, _iconEdited;

    /// <summary>Actions proposées : favoris et récents sans saisie, sinon le classement de la recherche.</summary>
    public ObservableCollection<CatalogItemViewModel> Suggestions { get; } = new();

    [ObservableProperty] string _query = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCreate))] string _title = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IconPreview), nameof(IconPreviewText), nameof(HasIconPreview))] string _icon = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCreate))] CatalogItemViewModel? _selected;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMessage))] string _message = "";

    /// <summary>La fenêtre doit se fermer (création faite ou annulation).</summary>
    public event Action? CloseRequested;

    /// <summary>true si une action est choisie et le titre rempli.</summary>
    public bool CanCreate => Selected != null && Title.Trim().Length > 0;
    /// <summary>true si un message d'aide ou d'erreur est affiché.</summary>
    public bool HasMessage => Message.Length > 0;
    /// <summary>Icône Fluent de l'aperçu, ou <see cref="SymbolRegular.Empty"/> quand le texte saisi n'est pas un nom d'icône connu.</summary>
    public SymbolRegular IconPreview => IconCatalog.TryResolve(Icon, out var symbol) ? symbol : SymbolRegular.Empty;
    /// <summary>Texte d'aperçu quand l'icône saisie est un emoji ou un court texte (non résolu en icône Fluent).</summary>
    public string IconPreviewText => IconCatalog.TryResolve(Icon, out _) ? "" : Icon.Trim();
    /// <summary>true si l'aperçu a quelque chose à montrer.</summary>
    public bool HasIconPreview => Icon.Trim().Length > 0;

    /// <summary>Panneau pour la case <paramref name="index"/> de <paramref name="place"/>.</summary>
    public QuickCreateViewModel(DeckDocument doc, IAppWindows windows, Place place, int index)
    {
        _doc = doc;
        _windows = windows;
        _place = place;
        _index = index;
        Refresh();
    }

    partial void OnQueryChanged(string value) => Refresh();

    partial void OnTitleChanged(string value)
    {
        if (!_syncing) _titleEdited = value.Trim().Length > 0;   // un titre effacé redevient automatique
    }

    partial void OnIconChanged(string value)
    {
        if (!_syncing) _iconEdited = value.Trim().Length > 0;
    }

    partial void OnSelectedChanged(CatalogItemViewModel? value)
    {
        if (value == null) return;
        _syncing = true;
        try
        {
            if (!_titleEdited) Title = value.Action.ButtonLabel;
            if (!_iconEdited) Icon = value.Action.Icon;
        }
        finally { _syncing = false; }
        Message = "";
    }

    /// <summary>Recalcule les suggestions ; la première est choisie d'office, donc « chrome » puis Entrée suffit.</summary>
    void Refresh()
    {
        var actions = string.IsNullOrWhiteSpace(Query) ? QuickCreate.Initial(_doc.Config) : QuickCreate.Rank(Query);
        Suggestions.Clear();
        foreach (var a in actions) Suggestions.Add(new CatalogItemViewModel(a, _doc.Config.Favorites.Contains(a.Id), _ => { }));
        Selected = Suggestions.FirstOrDefault();
        if (Suggestions.Count == 0 && !string.IsNullOrWhiteSpace(Query))
            Message = $"Aucune action ne correspond à « {Query.Trim()} ». Essaie un autre mot (chrome, volume, micro…).";
        else if (Suggestions.Count > 0 && Message.StartsWith("Aucune action", StringComparison.Ordinal)) Message = "";
    }

    /// <summary>Flèches haut / bas dans la zone de recherche : change l'action choisie sans quitter le clavier.</summary>
    public void MoveSelection(int delta)
    {
        if (Suggestions.Count == 0) return;
        int at = Selected == null ? -1 : Suggestions.IndexOf(Selected);
        Selected = Suggestions[Math.Clamp(at + delta, 0, Suggestions.Count - 1)];
    }

    /// <summary>
    /// Crée le bouton : une seule écriture dans le document. Si l'action demande encore un réglage (raccourci, chemin, adresse),
    /// l'éditeur rapide s'ouvre sur le bouton créé. Une case devenue occupée entre-temps n'est jamais écrasée.
    /// </summary>
    [RelayCommand]
    void Create()
    {
        if (!CanCreate) { Message = Selected == null ? "Choisis d'abord une action dans la liste." : "Donne un titre au bouton."; return; }
        if (_doc.Get(_place, _index) != null)
        {
            Message = "Cette case est déjà prise. Ferme ce panneau et choisis une case vide.";
            return;
        }
        var action = Selected!.Action;
        var button = action.CreateButton();
        button.Label = Title.Trim();
        if (Icon.Trim().Length > 0) button.Icon = Icon.Trim();
        var result = _doc.AddButton(_place, _index, button, $"Ajouter « {action.Name} »");
        if (!result.Ok) { Message = result.Message ?? "Création impossible."; return; }
        _doc.AddRecent(action.Id);
        if (QuickCreate.NeedsSetup(button, _doc.Config, _doc.FindProfile(_place.Profile)))
            _windows.OpenEditor(_place, _index, quick: true);
        else
            Notifier.Success($"« {button.Label} » ajouté", "Annuler", () => _doc.Undo());
        CloseRequested?.Invoke();
    }

    /// <summary>Ferme sans rien écrire.</summary>
    [RelayCommand]
    void Cancel() => CloseRequested?.Invoke();
}
