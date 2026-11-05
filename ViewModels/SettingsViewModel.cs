using System;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using StreamDak.Integrations;
using StreamDak.Platform.Windows;
using CommunityToolkit.Mvvm.Input;

namespace StreamDak.ViewModels;

/// <summary>Sauvegarde listée dans les paramètres.</summary>
/// <param name="Path">Chemin du fichier.</param>
/// <param name="Display">Date et taille affichées.</param>
/// <param name="RestoreCommand">Restaure cette sauvegarde (après confirmation).</param>
/// <param name="DeleteCommand">Supprime cette sauvegarde (après confirmation).</param>
public sealed record BackupItemViewModel(string Path, string Display, ICommand RestoreCommand, ICommand DeleteCommand);

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
    readonly IntegrationManager _integrationManager;
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
    [ObservableProperty] bool _economyMode;
    [ObservableProperty] double? _webIdleSeconds = ThemeOverrideKeys.DefaultWebIdleSeconds;
    [ObservableProperty] bool _liftDragEnabled = true;
    [ObservableProperty] double? _liftDelayMs = ThemeOverrideKeys.DefaultLiftDelayMs;

    /// <summary>Éditeur de thème : thèmes prédéfinis, couleurs, aperçu, avertissements.</summary>
    public ThemeEditorViewModel ThemeEditor { get; }
    [ObservableProperty] double? _backupLimit = ConfigStore.MaxBackups;
    [ObservableProperty] string _backupLimitHint = "";
    [ObservableProperty] string _diagnosticText = "";
    [ObservableProperty] string _consumptionText = "";

    /// <summary>Mises à jour par les releases GitHub : version, recherche, notes, installation.</summary>
    public UpdateService Updates { get; }

    /// <summary>Une carte par intégration (OBS, Hue…).</summary>
    public ObservableCollection<IntegrationCardViewModel> Integrations { get; } = new();
    /// <summary>
    /// Glisser-déposer (ou Alt+flèches) d'une carte d'intégration : elle prend le rang de la carte visée ; l'ordre est enregistré (réglage, hors historique)
    /// et suit au catalogue au prochain démarrage.
    /// </summary>
    public void MoveIntegration(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Integrations.Count || to >= Integrations.Count) return;
        Integrations.Move(from, to);
        var order = Integrations.Select(c => c.Id).ToList();
        _doc.UpdateSettings(c => c.IntegrationOrder = order, "Ordre des intégrations");
    }

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
        $"StreamDak {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?"} · .NET {Environment.Version.ToString(3)} · Windows {Environment.OSVersion.Version}";
    /// <summary>Dossier de configuration (affiché, ouvrable).</summary>
    public string ConfigDirectory => ConfigStore.Dir;

    static readonly (string Key, string Label)[] Themes = { ("system", "Suivre Windows"), ("dark", "Sombre"), ("amoled", "AMOLED"), ("light", "Clair"), ("tixia", "TIXIA") };

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
    public SettingsViewModel(ISettingsShell shell, DeckDocument doc, DisplayService displays, ConfigPersistence persistence,
        IntegrationManager integrations, ISecretStore secrets, UpdateService updates)
    {
        Updates = updates;
        _integrationManager = integrations;
        foreach (var integration in IntegrationOrder.Sort(integrations.Integrations, doc.Config.IntegrationOrder, i => i.Id)) Integrations.Add(new IntegrationCardViewModel(integration, integrations, doc, secrets));
        _shell = shell;
        _doc = doc;
        _displays = displays;
        _persistence = persistence;
        ThemeEditor = new ThemeEditorViewModel(doc);
        foreach (var (key, label) in Themes)
        {
            var k = key;
            ThemeChoices.Add(new ChoiceViewModel(label, () => SetTheme(k)));
        }
        Load();
        RefreshDiagnostic();
        _doc.Changed += OnChanged;
        _displays.Changed += RefreshDevices;
    }

    /// <summary>Se désabonne du document et des écrans (fermeture de la fenêtre).</summary>
    public void Dispose()
    {
        _doc.Changed -= OnChanged;
        _displays.Changed -= RefreshDevices;
        ThemeEditor.Dispose();
        foreach (var card in Integrations) card.Dispose();
    }

    void OnChanged(DocumentChange c)
    {
        // Un bouton modifié sur place ne touche à aucun réglage ; une carte d'intégration a déjà affiché ce qu'elle enregistre
        if (_loading || c.InPlace || c.Label == IntegrationCardViewModel.ChangeLabel) return;
        Load(refreshBackups: c.Kind == ChangeKind.Replaced);   // la liste des sauvegardes (lecture du disque) ne change qu'à une restauration ou un import
    }

    void Load(bool refreshBackups = true)
    {
        _loading = true;
        try
        {
            var c = _doc.Config;
            Fullscreen = c.Fullscreen;
            AlwaysOnTop = c.AlwaysOnTop;
            StartWithWindows = c.StartWithWindows;
            ReduceMotion = c.ReduceMotion;
            EconomyMode = c.EconomyMode;
            BackupLimit = ConfigStore.LimitOf(c);
            WebIdleSeconds = (int)ThemeOverrideKeys.WebIdle(c).TotalSeconds;
            LiftDragEnabled = !c.LiftDragDisabled;
            LiftDelayMs = ThemeOverrideKeys.LiftDelay(c);
            EmojiIcons = c.IconStyle == "emoji";
            CustomBackground = !string.Equals(c.Background, Theme.DefaultBackground, StringComparison.OrdinalIgnoreCase);
            Background = c.Background;
            BackgroundError = "";
            Columns = c.Columns; Rows = c.Rows; Spacing = c.Spacing;
            SystemReducesMotion = !SystemParameters.ClientAreaAnimation;
            foreach (var t in ThemeChoices.Zip(Themes)) t.First.IsSelected = t.Second.Key == c.Theme;
            RefreshDevices();
            if (refreshBackups) RefreshBackups();
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
                new RelayCommand(() => _ = RestoreAsync(path, b.Time)),
                new RelayCommand(() => _ = DeleteBackupAsync(path, b.Time))));
        }
        UpdateBackupHint();
    }

    /// <summary>Prévient quand des sauvegardes dépassent la limite : elles ne sont supprimées qu'à la prochaine sauvegarde (jamais pendant la saisie).</summary>
    void UpdateBackupHint()
    {
        int excess = Backups.Count - ConfigStore.LimitOf(_doc.Config);
        BackupLimitHint = excess > 0
            ? $"{excess} sauvegarde(s) dépassent la limite : les plus anciennes seront supprimées à la prochaine sauvegarde, ou tout de suite avec « Supprimer l'excédent »."
            : "";
    }

    async Task DeleteBackupAsync(string path, DateTime time)
    {
        if (!await _shell.ConfirmAsync("Supprimer une sauvegarde", $"Supprimer définitivement la sauvegarde du {time:d MMMM yyyy à HH:mm} ?", "Supprimer", danger: true)) return;
        try
        {
            if (!ConfigStore.DeleteBackup(path)) Notifier.Error("Sauvegarde non supprimée (fichier utilisé ?)");
        }
        catch (System.IO.InvalidDataException ex) { Notifier.Error(ex.Message); }
        RefreshBackups();
    }

    [RelayCommand]
    async Task DeleteAllBackups()
    {
        if (Backups.Count == 0) return;
        if (!await _shell.ConfirmAsync("Supprimer toutes les sauvegardes",
                $"Supprimer définitivement les {Backups.Count} sauvegardes ? La configuration actuelle n'est pas touchée.", "Tout supprimer", danger: true)) return;
        int n = ConfigStore.PruneBackups(0);
        Notifier.Info($"{n} sauvegarde(s) supprimée(s)");
        RefreshBackups();
    }

    [RelayCommand]
    void PruneExcessBackups()
    {
        int n = ConfigStore.PruneBackups(ConfigStore.LimitOf(_doc.Config));
        Notifier.Info($"{n} sauvegarde(s) supprimée(s)");
        RefreshBackups();
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
    partial void OnEconomyModeChanged(bool value) => Set(c => c.EconomyMode = value, "Mode économie");
    partial void OnWebIdleSecondsChanged(double? value)
    {
        if (_loading) return;
        int s = (int)Math.Clamp(value ?? ThemeOverrideKeys.DefaultWebIdleSeconds, ThemeOverrideKeys.MinWebIdleSeconds, ThemeOverrideKeys.MaxWebIdleSeconds);
        Set(c => c.WebIdleExitSeconds = s == ThemeOverrideKeys.DefaultWebIdleSeconds ? 0 : s, "Délai des pages web");   // la valeur par défaut n'est pas écrite
    }

    partial void OnLiftDragEnabledChanged(bool value) => Set(c => c.LiftDragDisabled = !value, "Déplacement par appui long");

    partial void OnLiftDelayMsChanged(double? value)
    {
        if (_loading) return;
        int ms = (int)Math.Clamp(value ?? ThemeOverrideKeys.DefaultLiftDelayMs, ThemeOverrideKeys.MinLiftDelayMs, ThemeOverrideKeys.MaxLiftDelayMs);
        Set(c => c.LiftDelayMs = ms == ThemeOverrideKeys.DefaultLiftDelayMs ? 0 : ms, "Seuil de l'appui long");
    }

    partial void OnBackupLimitChanged(double? value)
    {
        if (_loading) return;
        int limit = (int)Math.Clamp(value ?? ConfigStore.MaxBackups, ConfigStore.MinBackupLimit, ConfigStore.MaxBackupLimit);
        Set(c => c.BackupLimit = limit == ConfigStore.MaxBackups ? null : limit, "Nombre de sauvegardes");   // la valeur par défaut n'est pas écrite
        UpdateBackupHint();
    }

    /// <summary>Taille mémorisée de la fenêtre des paramètres, ou null.</summary>
    public (double Width, double Height)? SavedWindowSize =>
        _doc.Config is { SettingsWidth: { } w, SettingsHeight: { } h } ? (w, h) : null;

    /// <summary>Mémorise la taille de la fenêtre (réglage, hors historique).</summary>
    public void RememberWindowSize(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 560 || height < 520) return;
        if (_doc.Config.SettingsWidth is { } w && _doc.Config.SettingsHeight is { } h && Math.Abs(w - width) < 1 && Math.Abs(h - height) < 1) return;
        _doc.UpdateSettings(c => { c.SettingsWidth = Math.Round(width); c.SettingsHeight = Math.Round(height); }, "Taille des paramètres");
    }

    // ---------- diagnostic ----------

    static readonly DateTime Started = Process.GetCurrentProcess().StartTime;

    /// <summary>Texte de diagnostic : version, mémoire (application et WebView2), pauses de l'interface, intégrations, derniers avertissements. Sans secret.</summary>
    public string BuildDiagnostic()
    {
        using var self = Process.GetCurrentProcess();
        var content = (_doc.Config.Profiles.Count,
            _doc.Config.Profiles.Sum(p => p.Pages.Count),
            _doc.Config.Profiles.Sum(p => p.Pages.Sum(pg => DeckDocument.AllButtons(pg.Buttons).Count())));
        var input = new DiagnosticInput
        {
            Version = VersionText,
            WorkingSetBytes = self.WorkingSet64,
            WebView = WindowsPlatform.MeasureChildProcesses("msedgewebview2"),
            Uptime = DateTime.Now - Started,
            EconomyMode = _doc.Config.EconomyMode,
            Pauses = UiPauses.Snapshot(),
            Integrations = _integrationManager.Integrations
                .Select(i => new IntegrationStatus(i.Name, _integrationManager.IsEnabled(i.Id), i.State.ToString(), i.Error)).ToList(),
            RecentLog = Log.Recent(),
            Content = content,
        };
        return DiagnosticReport.Build(input);
    }

    /// <summary>Relit l'état (mémoire, pauses, intégrations) et l'affiche.</summary>
    [RelayCommand]
    void RefreshDiagnostic()
    {
        var text = BuildDiagnostic();
        DiagnosticText = text;
        ConsumptionText = text.Split('\n').FirstOrDefault(l => l.StartsWith("Mémoire", StringComparison.Ordinal)) ?? "";
    }

    /// <summary>Copie le diagnostic dans le presse-papiers.</summary>
    [RelayCommand]
    void CopyDiagnostic()
    {
        try
        {
            Clipboard.SetText(BuildDiagnostic());
            Notifier.Info("Diagnostic copié");
        }
        catch (System.Runtime.InteropServices.COMException ex) { Notifier.Error("Presse-papiers occupé : " + ex.Message); }
    }

    /// <summary>Remet à zéro le compteur de pauses.</summary>
    [RelayCommand]
    void ResetPauses()
    {
        UiPauses.Reset();
        RefreshDiagnostic();
    }

    /// <summary>Ouvre le journal.</summary>
    [RelayCommand]
    void OpenLog()
    {
        try { Process.Start(new ProcessStartInfo(System.IO.Path.Combine(ConfigStore.Dir, AppHost.LogFileName)) { UseShellExecute = true }); }
        catch (Exception ex) { Notifier.Error("Impossible d'ouvrir le journal : " + ex.Message); }
    }
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
        var path = _shell.PickSaveFile("Exporter la configuration", ProfilePackage.DialogFilter, $"StreamDak-{DateTime.Now:yyyyMMdd}{ProfilePackage.Extension}");
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
