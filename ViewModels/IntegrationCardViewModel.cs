using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TouchDeck.Integrations;
using Wpf.Ui.Controls;

namespace TouchDeck.ViewModels;

/// <summary>Un réglage d'intégration à renseigner (adresse, port, mot de passe) ; enregistré quand le champ perd le focus.</summary>
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
    bool _loading = true;

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
        _isEnabled = manager.IsEnabled(integration.Id);
        _loading = false;
        integration.StateChanged += OnStateChanged;
        Refresh();
    }

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

    /// <summary>true si TouchDeck se connecte à cette intégration au démarrage et se reconnecte si besoin.</summary>
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
        if (field.IsSecret) _secrets.Set(IntegrationManager.SecretKey(_integration.Id, field.Field.Key), field.Text);
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
        }, "Intégrations");
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

    /// <summary>Essaie de se connecter avec les réglages saisis et affiche le résultat.</summary>
    [RelayCommand]
    async Task TestAsync()
    {
        IsTesting = true;
        TestResult = "Test en cours…";
        try { TestResult = await _manager.TestAsync(_integration.Id); }
        catch (Exception ex) { TestResult = "Échec : " + ex.Message; }
        finally { IsTesting = false; Refresh(); }
    }

    /// <summary>Se désabonne de l'intégration (fermeture des paramètres).</summary>
    public void Dispose() => _integration.StateChanged -= OnStateChanged;
}
