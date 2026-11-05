using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StreamDak.ViewModels;

/// <summary>Thème prédéfini affiché avec un aperçu (fond, surface, accent, texte).</summary>
public sealed partial class ThemeCardViewModel : ObservableObject
{
    /// <summary>Valeur de <see cref="DeckConfig.Theme"/>.</summary>
    public string Key { get; }
    /// <summary>Nom affiché.</summary>
    public string Label { get; }
    /// <summary>Aperçu : fond de la fenêtre.</summary>
    public Brush Window { get; }
    /// <summary>Aperçu : surface.</summary>
    public Brush Surface { get; }
    /// <summary>Aperçu : accent.</summary>
    public Brush Accent { get; }
    /// <summary>Aperçu : texte.</summary>
    public Brush Text { get; }
    /// <summary>Choisit ce thème.</summary>
    public ICommand Command { get; }
    [ObservableProperty] bool _isSelected;

    /// <summary>Carte d'un thème, avec les couleurs de sa palette en aperçu.</summary>
    public ThemeCardViewModel(string key, string label, Palette palette, Action onSelect)
    {
        Key = key; Label = label;
        Window = Theme.B(palette.Window); Surface = Theme.B(palette.Surface); Accent = Theme.B(palette.Accent); Text = Theme.B(palette.Text);
        Command = new RelayCommand(onSelect);
    }
}

/// <summary>Une couleur modifiable du thème (accent, fond, surface, texte, succès, erreur).</summary>
public sealed partial class ThemeColorViewModel : ObservableObject
{
    readonly ThemeEditorViewModel _owner;
    bool _showing;

    /// <summary>Clé de <see cref="ThemeOverrideKeys"/>.</summary>
    public string Key { get; }
    /// <summary>Nom affiché.</summary>
    public string Label { get; }
    [ObservableProperty] string _hex = "";
    [ObservableProperty] Brush _swatch = Brushes.Transparent;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Status))] bool _isCustom;
    [ObservableProperty] string _error = "";

    /// <summary>« Personnalisée » ou « Couleur du thème ».</summary>
    public string Status => IsCustom ? "Personnalisée" : "Couleur du thème";
    /// <summary>Couleurs proposées en un toucher.</summary>
    public IReadOnlyList<string> Swatches => _owner.Swatches;
    /// <summary>Remet la couleur du thème.</summary>
    public ICommand ResetCommand { get; }
    /// <summary>Applique une couleur de la palette proposée (paramètre : #RRGGBB).</summary>
    public ICommand PickCommand { get; }

    internal ThemeColorViewModel(ThemeEditorViewModel owner, string key, string label)
    {
        _owner = owner; Key = key; Label = label;
        ResetCommand = new RelayCommand(() => owner.Reset(key));
        PickCommand = new RelayCommand<string>(hex => { if (hex != null) owner.Set(key, hex); });
    }

    internal void Show(string hex, bool custom)
    {
        _showing = true;
        Hex = hex; Swatch = Theme.B(hex); IsCustom = custom; Error = "";
        _showing = false;
    }

    partial void OnHexChanged(string value)
    {
        if (_showing) return;
        var v = value.Trim();
        if (!v.StartsWith('#')) v = "#" + v;
        if (ThemeOverrideKeys.IsHex(v)) { Error = ""; _owner.Set(Key, v); }
        else Error = "Couleur attendue : #RRGGBB";
    }
}

/// <summary>
/// Éditeur de thème des paramètres : thèmes prédéfinis avec aperçu, six couleurs modifiables, réinitialisation par couleur et globale, aperçu en direct
/// sur de vraies touches et avertissement de contraste. Tout passe par <see cref="DeckConfig.ThemeOverrides"/> et s'applique à chaud (ADR 0034).
/// </summary>
public sealed partial class ThemeEditorViewModel : ObservableObject, IDisposable
{
    readonly DeckDocument _doc;
    bool _busy;

    /// <summary>Thèmes prédéfinis.</summary>
    public ObservableCollection<ThemeCardViewModel> Cards { get; } = new();
    /// <summary>Couleurs modifiables.</summary>
    public ObservableCollection<ThemeColorViewModel> Colors { get; } = new();
    /// <summary>Touches d'aperçu, dessinées avec les couleurs actuelles.</summary>
    public ObservableCollection<TileFaceData> Preview { get; } = new();
    /// <summary>Contrastes insuffisants, un par ligne.</summary>
    [ObservableProperty] string _warnings = "";
    /// <summary>true s'il y a un avertissement.</summary>
    [ObservableProperty] bool _hasWarnings;
    /// <summary>true si au moins une couleur est personnalisée.</summary>
    [ObservableProperty] bool _anyCustom;

    /// <summary>Couleurs proposées en un toucher (cibles de 48 px).</summary>
    public IReadOnlyList<string> Swatches { get; } = new[]
    {
        "#FF7A1A", "#F97316", "#F59E0B", "#EAB308", "#22C55E", "#10B981", "#14B8A6", "#06B6D4",
        "#3B82F6", "#2F7DF6", "#6366F1", "#8B5CF6", "#D946EF", "#EC4899", "#EF4444", "#FF8A80",
        "#000000", "#1A120C", "#14161C", "#2D211A", "#F7EDE4", "#FFFFFF",
    };

    static readonly (string Key, string Label)[] Names =
    {
        ("accent", "Accent"), ("background", "Fond"), ("surface", "Surface"), ("text", "Texte"), ("success", "Succès"), ("danger", "Erreur"),
    };

    /// <summary>Crée l'éditeur et suit le thème appliqué (aperçu et valeurs affichées).</summary>
    public ThemeEditorViewModel(DeckDocument doc)
    {
        _doc = doc;
        void Card(string key, string label, Palette p) => Cards.Add(new ThemeCardViewModel(key, label, p, () => SetTheme(key)));
        Card("system", "Suivre Windows", Theme.DarkPalette);
        Card("dark", "Sombre", Theme.DarkPalette);
        Card("light", "Clair", Theme.LightPalette);
        Card("amoled", "AMOLED", Theme.AmoledPalette);
        Card("tixia", "TIXIA", Theme.TixiaPalette);
        foreach (var (key, label) in Names) Colors.Add(new ThemeColorViewModel(this, key, label));
        Refresh();
        _doc.Changed += OnChanged;
        Theme.Changed += OnThemeChanged;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _doc.Changed -= OnChanged;
        Theme.Changed -= OnThemeChanged;
    }

    void OnChanged(DocumentChange c) { if (!_busy && !c.InPlace) Refresh(); }

    void OnThemeChanged() => Refresh();

    void SetTheme(string key)
    {
        Write(c => c.Theme = key, "Thème");
    }

    internal void Set(string key, string hex) => Write(c =>
    {
        var map = c.ThemeOverrides != null ? new Dictionary<string, string>(c.ThemeOverrides, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
        map[key] = hex.ToUpperInvariant();
        c.ThemeOverrides = ThemeOverrideKeys.Sanitize(map);
    }, "Couleur du thème");

    internal void Reset(string key) => Write(c =>
    {
        if (c.ThemeOverrides == null) return;
        var map = new Dictionary<string, string>(c.ThemeOverrides, StringComparer.OrdinalIgnoreCase);
        map.Remove(key);
        c.ThemeOverrides = ThemeOverrideKeys.Sanitize(map);
    }, "Couleur du thème");

    /// <summary>Remet toutes les couleurs du thème.</summary>
    [RelayCommand]
    void ResetAll() => Write(c => c.ThemeOverrides = null, "Couleurs du thème");

    void Write(Action<DeckConfig> change, string label)
    {
        _busy = true;
        try { _doc.UpdateSettings(change, label); }
        finally { _busy = false; }
        Refresh();
    }

    static string BaseColor(Palette p, string key) => key switch
    {
        "accent" => p.Accent, "background" => p.Window, "surface" => p.Surface, "text" => p.Text, "success" => p.Success, _ => p.Danger,
    };

    /// <summary>Relit la configuration et le thème appliqué.</summary>
    public void Refresh()
    {
        var cfg = _doc.Config;
        foreach (var card in Cards) card.IsSelected = card.Key == cfg.Theme;
        foreach (var color in Colors)
        {
            bool custom = cfg.ThemeOverrides != null && cfg.ThemeOverrides.ContainsKey(color.Key);
            color.Show(custom ? cfg.ThemeOverrides![color.Key] : BaseColor(Theme.BasePalette, color.Key), custom);
        }
        AnyCustom = Colors.Any(c => c.IsCustom);
        var warnings = ThemeCustom.Warnings(Theme.Current);
        Warnings = string.Join(Environment.NewLine, warnings);
        HasWarnings = warnings.Count > 0;
        Preview.Clear();
        foreach (var (label, icon, color) in new[] { ("Accent", "Star24", "#0E7490"), ("Succès", "Checkmark24", "#15803D"), ("Alerte", "Warning24", "#B45309"), ("Média", "Play24", "#7C3AED") })
            Preview.Add(TileFaceData.From(new DeckButton { Label = label, Icon = icon, Color = color }));
    }
}
