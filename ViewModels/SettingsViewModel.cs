using System;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using TouchDeck.Platform.Windows;
using CommunityToolkit.Mvvm.Input;

namespace TouchDeck.ViewModels;

/// <summary>Sauvegarde listée dans les paramètres.</summary>
/// <param name="Path">Chemin du fichier.</param>
/// <param name="Display">Date et taille affichées.</param>
/// <param name="RestoreCommand">Restaure cette sauvegarde (après confirmation).</param>
public sealed record BackupItemViewModel(string Path, string Display, ICommand RestoreCommand);

/// <summary>Ce que les paramètres demandent à leur fenêtre : confirmations et sélecteurs de fichiers.</summary>
public interface ISettingsShell : IConfirm
{
    /// <summary>Choisit un fichier à ouvrir ; null si l'utilisateur annule.</summary>
    string? PickOpenFile(string title, string filter);
    /// <summary>Choisit un fichier à écrire ; null si l'utilisateur annule.</summary>
    string? PickSaveFile(string title, string filter, string defaultName);
}

/// <summary>Réglages de l'application. Tout ce qui est affiché agit réellement : chaque changement est appliqué tout de suite et enregistré.</summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    readonly DeckDocument _doc;
    readonly DisplayService _displays;
    readonly ConfigPersistence _persistence;
    readonly ISettingsShell _shell;
    bool _loading;

    [ObservableProperty] bool _fullscreen;
    [ObservableProperty] bool _alwaysOnTop;
    [ObservableProperty] bool _startWithWindows;
    [ObservableProperty] bool _reduceMotion;
    [ObservableProperty] bool _emojiIcons;
    [ObservableProperty] bool _customBackground;
    [ObservableProperty] string _background = "";
    [ObservableProperty] string _backgroundError = "";
    [ObservableProperty] double? _columns;
    [ObservableProperty] double? _rows;
    [ObservableProperty] double? _spacing;
    [ObservableProperty] ScreenChoice? _selectedScreen;
    [ObservableProperty] string _deviceText = "";
    [ObservableProperty] bool _deviceConnected;
    [ObservableProperty] bool _systemReducesMotion;

    /// <summary>Choix du thème : système, sombre, clair.</summary>
    public ObservableCollection<ChoiceViewModel> ThemeChoices { get; } = new();
    /// <summary>Écrans proposés pour le deck.</summary>
    public ObservableCollection<ScreenChoice> Screens { get; } = new();
    /// <summary>Sauvegardes disponibles, de la plus récente à la plus ancienne.</summary>
    public ObservableCollection<BackupItemViewModel> Backups { get; } = new();
    /// <summary>Description des écrans branchés.</summary>
    public ObservableCollection<string> Displays { get; } = new();

    /// <summary>Version de l'application (assembly).</summary>
    public string VersionText { get; } =
        $"TouchDeck {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?"} · .NET {Environment.Version.ToString(3)} · Windows {Environment.OSVersion.Version}";
    /// <summary>Dossier de configuration (affiché, ouvrable).</summary>
    public string ConfigDirectory => ConfigStore.Dir;

    static readonly (string Key, string Label)[] Themes = { ("system", "Suivre Windows"), ("dark", "Sombre"), ("amoled", "AMOLED"), ("light", "Clair") };

    /// <summary>Raccourcis clavier de l'éditeur, affichés dans les paramètres.</summary>
    public static readonly (string Keys, string What)[] Shortcuts =
    {
        ("Ctrl + Z", "Annuler la dernière modification"),
        ("Ctrl + Y  ou  Ctrl + Maj + Z", "Rétablir"),
        ("Ctrl + C / Ctrl + X / Ctrl + V", "Copier, couper, coller le bouton sélectionné"),
        ("Ctrl + D", "Dupliquer le bouton sélectionné"),
        ("Suppr", "Supprimer le bouton sélectionné"),
        ("F2", "Modifier le titre du bouton"),
        ("Entrée", "Ouvrir le dossier ou configurer le bouton"),
        ("Retour arrière", "Remonter au dossier parent"),
        ("Alt + flèches", "Déplacer le bouton dans la grille"),
        ("Flèches", "Changer de case"),
        ("Ctrl + F", "Rechercher dans le catalogue d'actions"),
        ("Maj + F10  ou  touche Menu", "Ouvrir le menu contextuel"),
        ("Échap", "Fermer un menu, annuler un glisser-déposer"),
    };

    /// <summary>Paramètres sur le document partagé ; suit ses modifications et les changements d'écran.</summary>
    public SettingsViewModel(ISettingsShell shell, DeckDocument doc, DisplayService displays, ConfigPersistence persistence)
    {
        _shell = shell;
        _doc = doc;
        _displays = displays;
        _persistence = persistence;
        foreach (var (key, label) in Themes)
        {
            var k = key;
            ThemeChoices.Add(new ChoiceViewModel(label, () => SetTheme(k)));
        }
        Load();
        _doc.Changed += OnChanged;
        _displays.Changed += RefreshDevices;
    }

    /// <summary>Se désabonne du document et des écrans (fermeture de la fenêtre).</summary>
    public void Dispose()
    {
        _doc.Changed -= OnChanged;
        _displays.Changed -= RefreshDevices;
    }

    void OnChanged(DocumentChange c)
    {
        if (!_loading) Load();
    }

    void Load()
    {
        _loading = true;
        try
        {
            var c = _doc.Config;
            Fullscreen = c.Fullscreen;
            AlwaysOnTop = c.AlwaysOnTop;
            StartWithWindows = c.StartWithWindows;
            ReduceMotion = c.ReduceMotion;
            EmojiIcons = c.IconStyle == "emoji";
            CustomBackground = !string.Equals(c.Background, Theme.DefaultBackground, StringComparison.OrdinalIgnoreCase);
            Background = c.Background;
            BackgroundError = "";
            Columns = c.Columns; Rows = c.Rows; Spacing = c.Spacing;
            SystemReducesMotion = !SystemParameters.ClientAreaAnimation;
            foreach (var t in ThemeChoices.Zip(Themes)) t.First.IsSelected = t.Second.Key == c.Theme;
            RefreshDevices();
            RefreshBackups();
        }
        finally { _loading = false; }
    }

    void RefreshDevices()
    {
        var wasLoading = _loading;
        _loading = true;
        var cfg = _doc.Config;
        var (choices, selected) = ScreenChoice.Build(cfg, _displays.All(), "Automatique (premier écran secondaire)");
        Screens.Clear();
        foreach (var s in choices) Screens.Add(s);
        SelectedScreen = selected;
        (DeviceText, DeviceConnected) = _displays.Describe(cfg);
        var target = _displays.ResolveTarget(cfg);
        Displays.Clear();
        foreach (var d in _displays.All()) Displays.Add($"{d}{(d == target ? "  — écran du deck" : "")}");
        _loading = wasLoading;
    }

    void RefreshBackups()
    {
        Backups.Clear();
        foreach (var b in ConfigStore.ListBackups())
        {
            var path = b.Path;
            Backups.Add(new BackupItemViewModel(path, $"{b.Time:d MMM yyyy à HH:mm:ss}  ·  {Math.Max(1, b.Size / 1024)} Ko",
                new RelayCommand(() => _ = RestoreAsync(path, b.Time))));
        }
    }

    void Set(Action<DeckConfig> change, string label)
    {
        if (_loading) return;
        _loading = true;
        try { _doc.UpdateSettings(change, label); }
        finally { _loading = false; }
    }

    void SetTheme(string key)
    {
        Set(c => c.Theme = key, "Thème");
        foreach (var t in ThemeChoices.Zip(Themes)) t.First.IsSelected = t.Second.Key == key;
    }

    partial void OnFullscreenChanged(bool value) => Set(c => c.Fullscreen = value, "Plein écran");
    partial void OnAlwaysOnTopChanged(bool value) => Set(c => c.AlwaysOnTop = value, "Toujours au premier plan");
    partial void OnReduceMotionChanged(bool value) => Set(c => c.ReduceMotion = value, "Animations");
    partial void OnEmojiIconsChanged(bool value) => Set(c => c.IconStyle = value ? "emoji" : "fluent", "Style des icônes");

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading) return;
        if (!AutoStart.Set(value))
        {
            Notifier.Error("Impossible de modifier le démarrage automatique de Windows");
            _loading = true; StartWithWindows = !value; _loading = false;
            return;
        }
        Set(c => c.StartWithWindows = value, "Démarrage");
    }

    partial void OnColumnsChanged(double? value) => Set(c => c.Columns = (int)Math.Clamp(value ?? 0, 0, 20), "Colonnes");
    partial void OnRowsChanged(double? value) => Set(c => c.Rows = (int)Math.Clamp(value ?? 0, 0, 20), "Lignes");
    partial void OnSpacingChanged(double? value) => Set(c => c.Spacing = Math.Clamp(value ?? 14, 0, 80), "Espacement");

    partial void OnSelectedScreenChanged(ScreenChoice? value)
    {
        if (_loading || value == null || value.IsCurrent(_doc.Config)) return;
        Set(value.ApplyTo, "Écran du deck");
        (DeviceText, DeviceConnected) = _displays.Describe(_doc.Config);
    }

    partial void OnCustomBackgroundChanged(bool value)
    {
        if (_loading) return;
        if (!value) { Set(c => c.Background = Theme.DefaultBackground, "Fond"); Background = Theme.DefaultBackground; BackgroundError = ""; }
        else if (string.Equals(_doc.Config.Background, Theme.DefaultBackground, StringComparison.OrdinalIgnoreCase))
        {
            Background = "#1C1C1F";
            Set(c => c.Background = "#1C1C1F", "Fond");
        }
    }

    partial void OnBackgroundChanged(string value)
    {
        if (_loading || !CustomBackground) return;
        if (!Theme.TryBrush(value, out _)) { BackgroundError = "Couleur non valide. Exemple : #1C1C1F"; return; }
        BackgroundError = "";
        Set(c => c.Background = value.Trim(), "Fond");
    }

    // ---------- sauvegardes ----------

    [RelayCommand]
    void ExportConfig()
    {
        var path = _shell.PickSaveFile("Exporter la configuration", ProfilePackage.DialogFilter, $"TouchDeck-{DateTime.Now:yyyyMMdd}{ProfilePackage.Extension}");
        if (path != null) TransferService.ExportConfig(_doc.Config, path);
    }

    [RelayCommand]
    async Task ImportConfig()
    {
        var path = _shell.PickOpenFile("Importer une configuration", ProfilePackage.ConfigDialogFilter);
        if (path == null) return;
        if (await TransferService.ImportConfigAsync(_shell, _doc, path)) { _loading = false; Load(); }
    }

    async Task RestoreAsync(string path, DateTime time)
    {
        if (!await _shell.ConfirmAsync("Restaurer une sauvegarde",
                $"Remplacer la configuration actuelle par la sauvegarde du {time:d MMMM yyyy à HH:mm} ? La configuration actuelle est sauvegardée avant, et tu peux annuler.",
                "Restaurer", danger: true)) return;
        TransferService.RestoreBackup(_doc, path);
        Load();
    }

    [RelayCommand]
    void BackupNow()
    {
        var path = ConfigStore.CreateBackup(_doc.Config);
        Notifier.Info(path == null ? "La dernière sauvegarde est déjà à jour" : "Sauvegarde créée");
        RefreshBackups();
    }

    [RelayCommand]
    void OpenFolder()
    {
        try { Process.Start(new ProcessStartInfo(ConfigStore.Dir) { UseShellExecute = true }); }
        catch (Exception ex) { Notifier.Error("Impossible d'ouvrir le dossier : " + ex.Message); }
    }

    [RelayCommand]
    void OpenConfigFile()
    {
        try
        {
            _persistence.Flush();
            Process.Start(new ProcessStartInfo(ConfigStore.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex) { Notifier.Error("Impossible d'ouvrir le fichier : " + ex.Message); }
    }

    [RelayCommand] void Reload() => _persistence.ReloadFromDisk();
}
