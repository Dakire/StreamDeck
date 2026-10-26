using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StreamDak;

/// <summary>Où vit l'application : dossiers de configuration, d'installation et de travail de la mise à jour, version et processus en cours.</summary>
/// <param name="ConfigDir">Dossier de configuration (update.json, update-state.json).</param>
/// <param name="AppDir">Dossier d'installation (à côté de l'exécutable).</param>
/// <param name="WorkDir">Dossier privé des téléchargements, de la sauvegarde et du journal de mise à jour.</param>
/// <param name="Current">Version en cours.</param>
/// <param name="ExePath">Exécutable en cours.</param>
/// <param name="ProcessId">Processus en cours.</param>
public sealed record UpdateEnvironment(string ConfigDir, string AppDir, string WorkDir, SemVer Current, string ExePath, int ProcessId)
{
    /// <summary>true si l'application a été posée par l'installateur (son désinstallateur est là) : seule une installation se met à jour toute seule.</summary>
    public bool IsInstalled => File.Exists(Path.Combine(AppDir, "unins000.exe"));

    /// <summary>
    /// Version de l'application en cours d'après son assembly (<c>InformationalVersion</c> « 1.4.0+commit », sinon la version d'assembly) ; 0.0.0 si elle est illisible,
    /// de sorte que n'importe quelle release soit alors plus récente.
    /// </summary>
    public static SemVer CurrentVersion()
    {
        var assembly = System.Reflection.Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;
        var info = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        if (SemVer.TryParse(info, out var fromInfo)) return fromInfo;
        var v = assembly.GetName().Version;
        return v != null && SemVer.TryParse($"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}", out var fromAssembly) ? fromAssembly : SemVer.Parse("0.0.0");
    }
}

/// <summary>
/// Mises à jour par les releases GitHub : vérification manuelle ou automatique (une fois par jour au plus, en arrière-plan, désactivable), notes de version,
/// téléchargement vérifié (SHA-256 de checksums.txt, signature Authenticode), installation silencieuse avec sauvegarde et restauration. <b>Rien ne s'installe sans
/// l'accord de l'utilisateur</b> : la vérification automatique n'allume qu'un indicateur discret. Ne bloque jamais le démarrage.
/// </summary>
public sealed partial class UpdateService : ObservableObject, IDisposable
{
    /// <summary>Intervalle minimal entre deux vérifications automatiques.</summary>
    public static readonly TimeSpan AutoInterval = TimeSpan.FromHours(24);
    /// <summary>Attente après le démarrage avant la vérification automatique (le démarrage n'est jamais ralenti).</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    readonly UpdateEnvironment _env;
    readonly UpdateChecker _checker;
    readonly UpdateDownloader _downloader;
    readonly ISignatureVerifier _verifier;
    readonly IUpdateApplier _applier;
    readonly Action _exit;
    readonly Func<DateTime> _utcNow;
    readonly SemaphoreSlim _busy = new(1, 1);
    UpdateSettings _settings;
    UpdateState _state;
    CancellationTokenSource? _download;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasUpdate), nameof(NewVersionText), nameof(Notes), nameof(ShowDetails))] ReleaseInfo? _release;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasUpdate), nameof(ShowDetails))] bool _ignored;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowDetails))] bool _dismissed;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCheck)), NotifyCanExecuteChangedFor(nameof(CheckNowCommand))] bool _isChecking;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCheck), nameof(ShowDetails))] bool _isInstalling;
    [ObservableProperty] string _statusText = "";
    [ObservableProperty] string _errorText = "";
    [ObservableProperty] double? _progress;
    [ObservableProperty] string _progressText = "";
    [ObservableProperty] bool _beta;
    [ObservableProperty] bool _autoCheck = true;

    /// <summary>Service de mise à jour ; tous les accès au réseau, au disque, aux signatures et au lancement sont injectés (les tests les remplacent).</summary>
    public UpdateService(UpdateEnvironment environment, UpdateChecker checker, UpdateDownloader downloader, ISignatureVerifier verifier, IUpdateApplier applier, Action exit, Func<DateTime>? utcNow = null)
    {
        _env = environment;
        _checker = checker;
        _downloader = downloader;
        _verifier = verifier;
        _applier = applier;
        _exit = exit;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _settings = UpdateSettings.Load(_env.ConfigDir, _env.AppDir);
        _state = UpdateState.Load(_env.ConfigDir);
        _beta = _settings.Beta;
        _autoCheck = _settings.AutoCheck;
        ReportPreviousFailure();
        CleanOldDownloads();
    }

    partial void OnBetaChanged(bool value) => SetBeta(value);
    partial void OnAutoCheckChanged(bool value) => SetAutoCheck(value);

    [RelayCommand(CanExecute = nameof(CanCheck))] Task CheckNow() => CheckAsync(manual: true);
    [RelayCommand] Task InstallNow() => InstallAsync();
    [RelayCommand] void PostponeUpdate() => Later();
    [RelayCommand] void IgnoreUpdate() => Ignore();
    [RelayCommand] void CancelDownload() => Cancel();

    /// <summary>Ouvre la page de la release sur GitHub (navigateur par défaut), pour une copie non posée par l'installateur.</summary>
    [RelayCommand]
    void OpenReleasePage()
    {
        if (Release is not { } release) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(release.PageUrl.AbsoluteUri) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex) { ErrorText = "Impossible d'ouvrir le navigateur : " + ex.Message; }
    }

    /// <summary>Version en cours (« 1.0.0 »).</summary>
    public string CurrentVersionText => _env.Current.ToString();
    /// <summary>true si un dépôt de releases est configuré (update.json ou valeurs livrées).</summary>
    public bool IsConfigured => _settings.Repository != null;
    /// <summary>Dépôt suivi (« Dakire/StreamDeck »), ou une chaîne vide.</summary>
    public string RepositoryText => _settings.Repository is { } r ? $"{r.Owner}/{r.Repo}" : "";
    /// <summary>true si une version plus récente existe et n'a pas été ignorée : l'indicateur discret du deck.</summary>
    public bool HasUpdate => Release != null && !Ignored;
    /// <summary>« 1.4.0 » (ou « 1.5.0-beta.1 »), la version proposée.</summary>
    public string NewVersionText => Release?.Version.ToString() ?? "";
    /// <summary>true tant qu'une vérification n'est pas en cours : le bouton « Rechercher » est actif.</summary>
    public bool CanCheck => !IsChecking && !IsInstalling;
    /// <summary>true quand le panneau « version disponible » (notes, boutons) s'affiche : une version existe, ni ignorée ni repoussée, ou une installation est en cours.</summary>
    public bool ShowDetails => (Release != null && !Ignored && !Dismissed) || IsInstalling;
    /// <summary>Notes de version de la release proposée, en blocs prêts à afficher.</summary>
    public IReadOnlyList<MarkdownBlock> Notes => Release == null ? Array.Empty<MarkdownBlock>() : SimpleMarkdown.Parse(Release.Notes);
    /// <summary>true si cette installation peut se mettre à jour toute seule (posée par l'installateur).</summary>
    public bool CanInstall => _env.IsInstalled;

    /// <summary>
    /// Une vérification. <paramref name="manual"/> : demandée par l'utilisateur (toujours faite, le résultat est dit) ; sinon automatique (désactivable, une fois par jour,
    /// silencieuse). Ne lève jamais d'exception réseau.
    /// </summary>
    public async Task CheckAsync(bool manual, CancellationToken cancellation = default)
    {
        if (!manual && (!_settings.AutoCheck || !IsConfigured || !UpdateChecker.IsDue(_state, _utcNow(), AutoInterval))) return;
        if (!await _busy.WaitAsync(0, cancellation)) return;   // une vérification ou une installation tourne déjà
        IsChecking = true;
        ErrorText = "";
        if (manual) StatusText = "Recherche en cours…";
        try
        {
            var result = await _checker.CheckAsync(_env.Current, _settings, _state, cancellation);
            _state.Save(_env.ConfigDir);
            switch (result.Status)
            {
                case UpdateStatus.Available:
                    Release = result.Release;
                    Ignored = _state.IgnoredVersion != null && string.Equals(_state.IgnoredVersion, result.Release!.Tag, StringComparison.OrdinalIgnoreCase);
                    Dismissed = false;
                    StatusText = Ignored ? $"La version {NewVersionText} est ignorée." : $"Version {NewVersionText} disponible (tu as la {CurrentVersionText}).";
                    break;
                case UpdateStatus.UpToDate:
                    Release = null;
                    StatusText = result.Message ?? $"Tu as la dernière version ({CurrentVersionText}).";
                    break;
                case UpdateStatus.Disabled:
                    StatusText = result.Message ?? "";
                    break;
                default:
                    if (manual) ErrorText = result.Message ?? "La vérification a échoué.";
                    StatusText = manual ? "" : StatusText;
                    Log.Warn("Mises à jour : " + result.Message);
                    break;
            }
        }
        finally
        {
            IsChecking = false;
            _busy.Release();
        }
    }

    /// <summary>Lance la vérification automatique après <see cref="StartupDelay"/>, sans jamais attendre ni lever : le démarrage n'est pas ralenti.</summary>
    public void StartBackgroundCheck(TimeSpan? delay = null) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay ?? StartupDelay);
                await CheckAsync(manual: false);
            }
            catch (Exception ex) { Log.Warn($"Mises à jour : vérification automatique abandonnée ({ex.Message})"); }
        });

    /// <summary>Choisit le canal : bêta (pré-versions comprises) ou stable. La prochaine vérification refait la requête.</summary>
    public void SetBeta(bool beta)
    {
        if (_settings.Beta == beta) return;
        _settings.Beta = beta;
        Beta = beta;
        Release = null;
        _state.ETag = _state.CachedBody = null;
        Persist();
    }

    /// <summary>Active ou désactive la vérification automatique au démarrage.</summary>
    public void SetAutoCheck(bool enabled)
    {
        if (_settings.AutoCheck == enabled) return;
        _settings.AutoCheck = enabled;
        AutoCheck = enabled;
        Persist();
    }

    void Persist()
    {
        try { _settings.Save(_env.ConfigDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"update.json non écrit : {ex.Message}"); }
        _state.Save(_env.ConfigDir);
    }

    /// <summary>« Plus tard » : le panneau se replie ; l'indicateur reste, la version est reproposée à la prochaine vérification.</summary>
    public void Later() => Dismissed = true;

    /// <summary>« Ignorer cette version » : plus d'indicateur ni de panneau pour ce tag (une version suivante est proposée normalement).</summary>
    public void Ignore()
    {
        if (Release == null) return;
        _state.IgnoredVersion = Release.Tag;
        Ignored = true;
        _state.Save(_env.ConfigDir);
        StatusText = $"La version {NewVersionText} est ignorée.";
    }

    /// <summary>Annule le téléchargement en cours (le fichier partiel est gardé pour une reprise).</summary>
    public void Cancel() => _download?.Cancel();

    /// <summary>
    /// « Installer maintenant » : télécharge, vérifie (SHA-256, signature), puis confie l'installation à l'applicateur et ferme l'application. Toute erreur est dite dans
    /// <see cref="ErrorText"/> ; l'application reste ouverte, rien n'est installé.
    /// </summary>
    public async Task InstallAsync()
    {
        if (Release is not { } release || _settings.Repository is not { } repo) return;
        if (!_env.IsInstalled)
        {
            ErrorText = "Cette copie n'a pas été posée par l'installateur : télécharge et lance le nouvel installateur depuis la page de la release.";
            return;
        }
        if (!await _busy.WaitAsync(0)) return;
        IsInstalling = true;
        ErrorText = "";
        Progress = null;
        ProgressText = "Téléchargement…";
        _download = new CancellationTokenSource();
        bool launched = false;
        try
        {
            var flavor = InstallFlavor.Read(_env.AppDir);
            var folder = Path.Combine(_env.WorkDir, release.Version.ToString());
            var reporter = new Progress<DownloadProgress>(p =>
            {
                Progress = p.Fraction;
                ProgressText = p.Total is { } total ? $"Téléchargement : {WidgetFormat.Bytes(p.Received, CultureInfo.CurrentCulture)} sur {WidgetFormat.Bytes(total, CultureInfo.CurrentCulture)}" : "Téléchargement…";
            });
            var installer = await _downloader.DownloadAsync(repo, release, flavor, folder, reporter, _download.Token);

            ProgressText = "Vérification de la signature…";
            Progress = null;
            var refusal = await Task.Run(() => SignaturePolicy.Refusal(_verifier.Verify(installer), _verifier.Verify(_env.ExePath)));
            if (refusal != null)
            {
                File.Delete(installer);
                throw new UpdateIntegrityException(refusal);
            }

            ProgressText = "Installation : l'application va se fermer puis se relancer…";
            _applier.Launch(new UpdatePlan(installer, _env.AppDir.TrimEnd('\\'), _env.ExePath, _env.ProcessId, _env.WorkDir));
            launched = true;
        }
        catch (OperationCanceledException) { ErrorText = "Téléchargement annulé : il reprendra où il en était."; }
        catch (UpdateIntegrityException ex) { ErrorText = ex.Message; Log.Warn("Mises à jour : " + ex.Message); }
        catch (InvalidOperationException ex) { ErrorText = ex.Message; Log.Warn("Mises à jour : " + ex.Message); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ErrorText = "Le téléchargement ou le lancement a échoué : " + ex.Message;
            Log.Warn("Mises à jour : " + ex.Message);
        }
        finally
        {
            _download.Dispose();
            _download = null;
            IsInstalling = false;
            Progress = null;
            ProgressText = "";
            _busy.Release();
        }
        if (launched) _exit();
    }

    /// <summary>Si la mise à jour précédente a échoué (l'ancienne version a été restaurée), le dit une fois.</summary>
    void ReportPreviousFailure()
    {
        try
        {
            var marker = Path.Combine(_env.WorkDir, UpdateApplier.FailedMarker);
            if (!File.Exists(marker)) return;
            var reason = File.ReadAllText(marker).Trim();
            File.Delete(marker);
            ErrorText = "La dernière mise à jour a échoué : l'ancienne version a été restaurée. " + reason;
            Log.Warn("Mises à jour : échec de la précédente installation : " + reason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* rien à dire si le marqueur est illisible */ }
    }

    /// <summary>Supprime les téléchargements des versions déjà installées (ou plus anciennes) : le dossier de travail ne grossit pas.</summary>
    void CleanOldDownloads()
    {
        try
        {
            if (!Directory.Exists(_env.WorkDir)) return;
            foreach (var dir in Directory.EnumerateDirectories(_env.WorkDir))
                if (SemVer.TryParse(Path.GetFileName(dir), out var version) && version <= _env.Current) Directory.Delete(dir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Mises à jour : nettoyage impossible ({ex.Message})"); }
    }

    /// <summary>Annule un téléchargement en cours et libère le verrou (arrêt de l'application).</summary>
    public void Dispose()
    {
        try { _download?.Cancel(); }
        catch (ObjectDisposedException) { /* déjà terminé */ }
        _busy.Dispose();
    }
}
