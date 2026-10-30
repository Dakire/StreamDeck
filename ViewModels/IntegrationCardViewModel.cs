using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDak.Integrations;
using Wpf.Ui.Controls;

namespace StreamDak.ViewModels;

/// <summary>Un réglage d'intégration à renseigner (adresse, port, mot de passe) ; enregistré 600 ms après la dernière frappe, ou à la fermeture des paramètres.</summary>
public sealed partial class IntegrationFieldViewModel : ObservableObject
{
    readonly Action<IntegrationFieldViewModel> _commit;

    /// <summary>Champ de <paramref name="field"/> ; <paramref name="commit"/> enregistre la valeur saisie.</summary>
    public IntegrationFieldViewModel(SettingField field, string text, Action<IntegrationFieldViewModel> commit)
    {
        Field = field;
        _text = text;
        _commit = commit;
    }

    /// <summary>Description du réglage.</summary>
    public SettingField Field { get; }
    /// <summary>Libellé affiché.</summary>
    public string Label => Field.Label;
    /// <summary>Aide affichée sous le champ.</summary>
    public string Hint => Field.Hint ?? "";
    /// <summary>true si l'aide existe.</summary>
    public bool HasHint => Field.Hint != null;
    /// <summary>true pour un champ masqué (mot de passe, jeton).</summary>
    public bool IsSecret => Field.Kind == SettingKind.Secret;
    /// <summary>true pour un champ ordinaire.</summary>
    public bool IsPlain => !IsSecret;
    /// <summary>Valeur saisie.</summary>
    [ObservableProperty] string _text;

    partial void OnTextChanged(string value) => _commit(this);
}

/// <summary>Carte d'une intégration dans les paramètres : activation, réglages, état de la connexion, test.</summary>
public sealed partial class IntegrationCardViewModel : ObservableObject, IDisposable
{
    readonly IIntegration _integration;
    readonly IntegrationManager _manager;
    readonly DeckDocument _doc;
    readonly ISecretStore _secrets;
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer _commitTimer;
    readonly HashSet<IntegrationFieldViewModel> _pending = new();
    bool _loading = true;

    /// <summary>Attente après la dernière frappe avant d'enregistrer : un mot de passe tapé ne relance plus la connexion à chaque caractère.</summary>
    public static readonly TimeSpan CommitDelay = TimeSpan.FromMilliseconds(600);

    /// <summary>Libellé des modifications du document faites par les cartes (les paramètres ne se rechargent pas pour elles).</summary>
    public const string ChangeLabel = "Intégrations";

    /// <summary>Carte de <paramref name="integration"/> ; lit et écrit ses réglages dans le document et le magasin de secrets.</summary>
    public IntegrationCardViewModel(IIntegration integration, IntegrationManager manager, DeckDocument doc, ISecretStore secrets)
    {
        _integration = integration;
        _manager = manager;
        _doc = doc;
        _secrets = secrets;
        var settings = manager.SettingsFor(integration);
        foreach (var field in integration.Fields)
        {
            var text = field.Kind == SettingKind.Secret ? settings.Secret(field.Key) : settings.Get(field.Key, field.Default ?? "");
            Fields.Add(new IntegrationFieldViewModel(field, text, Commit));
        }
        _commitTimer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = CommitDelay };
        _commitTimer.Tick += (_, _) => FlushPending();
        _isEnabled = manager.IsEnabled(integration.Id);
        _loading = false;
        integration.StateChanged += OnStateChanged;
        Refresh();
    }

    /// <summary>Identifiant stable de l'intégration (« obs », « sonar »).</summary>
    public string Id => _integration.Id;
    /// <summary>Nom de l'intégration.</summary>
    public string Name => _integration.Name;
    /// <summary>Icône Fluent de l'intégration.</summary>
    public SymbolRegular Symbol => IconCatalog.TryResolve(_integration.Icon, out var s) ? s : SymbolRegular.PlugConnected24;
    /// <summary>Prérequis et mode d'emploi.</summary>
    public string Help => _integration.Help;
    /// <summary>Réglages à renseigner.</summary>
    public ObservableCollection<IntegrationFieldViewModel> Fields { get; } = new();
    /// <summary>true s'il y a des réglages à renseigner.</summary>
    public bool HasFields => Fields.Count > 0;

    /// <summary>true si l'intégration s'associe à un appareil par une démarche physique (bouton du pont Hue).</summary>
    public bool CanPair => _integration is IPairableIntegration;
    /// <summary>Texte du bouton d'association.</summary>
    public string PairLabel => (_integration as IPairableIntegration)?.PairLabel ?? "";
    /// <summary>Marche à suivre avant d'associer.</summary>
    public string PairHelp => (_integration as IPairableIntegration)?.PairHelp ?? "";

    /// <summary>true si StreamDak se connecte à cette intégration au démarrage et se reconnecte si besoin.</summary>
    [ObservableProperty] bool _isEnabled;
    /// <summary>État de la connexion, en toutes lettres (jamais la couleur seule).</summary>
    [ObservableProperty] string _statusText = "";
    /// <summary>Icône de l'état.</summary>
    [ObservableProperty] SymbolRegular _statusSymbol = SymbolRegular.PlugDisconnected24;
    /// <summary>Résultat du dernier test de connexion.</summary>
    [ObservableProperty] string _testResult = "";
    /// <summary>true pendant un test de connexion.</summary>
    [ObservableProperty] bool _isTesting;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_loading) return;
        Save();
        Refresh();
    }

    void Commit(IntegrationFieldViewModel field)
    {
        if (_loading) return;
        _pending.Add(field);
        _commitTimer.Stop();
        _commitTimer.Start();
    }

    /// <summary>Enregistre tout de suite les champs saisis depuis le dernier enregistrement (fin de la frappe, association, test, fermeture).</summary>
    public void FlushPending()
    {
        _commitTimer.Stop();
        if (_pending.Count == 0) return;
        foreach (var field in _pending.Where(f => f.IsSecret)) _secrets.Set(IntegrationManager.SecretKey(_integration.Id, field.Field.Key), field.Text);
        _pending.Clear();
        Save();
        _manager.ApplyAll();   // un secret modifié ne change pas le document : on relit explicitement
    }

    void Save()
    {
        _doc.UpdateSettings(c =>
        {
            c.Integrations ??= new();
            var values = Fields.Where(f => !f.IsSecret && !string.IsNullOrWhiteSpace(f.Text)).ToDictionary(f => f.Field.Key, f => f.Text.Trim());
            c.Integrations[_integration.Id] = new IntegrationConfig { Enabled = IsEnabled, Values = values.Count == 0 ? null : values };
        }, ChangeLabel);
    }

    void OnStateChanged() => _ui.BeginInvoke(Refresh);

    void Refresh()
    {
        var state = IsEnabled ? _integration.State : ConnectionState.Disabled;
        (StatusSymbol, StatusText) = state switch
        {
            ConnectionState.Connected => (SymbolRegular.CheckmarkCircle24, "Connecté"),
            ConnectionState.Connecting => (SymbolRegular.ArrowSync24, "Connexion en cours…"),
            ConnectionState.Error => (SymbolRegular.ErrorCircle24, $"Échec : {_integration.Error}. Nouvelle tentative automatique."),
            ConnectionState.Disconnected => (SymbolRegular.PlugDisconnected24, "Déconnecté, nouvelle tentative automatique"),
            _ => (SymbolRegular.Circle24, "Désactivé"),
        };
    }

    /// <summary>Associe StreamDak à l'appareil et enregistre la clé obtenue comme secret.</summary>
    [RelayCommand]
    async Task PairAsync()
    {
        if (_integration is not IPairableIntegration pairable) return;
        IsTesting = true;
        TestResult = "Association en cours…";
        try
        {
            var current = new IntegrationSettings
            {
                Values = Fields.Where(f => !f.IsSecret).ToDictionary(f => f.Field.Key, f => f.Text.Trim()),
                Secrets = Fields.Where(f => f.IsSecret && f.Field.Key != pairable.PairedSecretField).ToDictionary(f => f.Field.Key, f => f.Text),
            };
            var key = await pairable.PairAsync(current, default);
            var field = Fields.First(f => f.Field.Key == pairable.PairedSecretField);
            field.Text = key;
            FlushPending();     // enregistre le secret et relance la connexion
            TestResult = "Association réussie.";
        }
        catch (Exception ex) { TestResult = "Échec : " + ex.Message; }
        finally { IsTesting = false; }
    }

    /// <summary>Essaie de se connecter avec les réglages saisis et affiche le résultat.</summary>
    [RelayCommand]
    async Task TestAsync()
    {
        IsTesting = true;
        TestResult = "Test en cours…";
        FlushPending();   // le test porte sur ce qui vient d'être saisi
        try { TestResult = await _manager.TestAsync(_integration.Id); }
        catch (Exception ex) { TestResult = "Échec : " + ex.Message; }
        finally { IsTesting = false; Refresh(); }
    }

    /// <summary>Enregistre la saisie en cours et se désabonne de l'intégration (fermeture des paramètres).</summary>
    public void Dispose()
    {
        FlushPending();
        _integration.StateChanged -= OnStateChanged;
    }
}
