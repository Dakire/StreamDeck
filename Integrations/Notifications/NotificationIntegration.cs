using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Integrations.Notifications;

/// <summary>
/// Badges de notifications : un badge rouge (nombre de non lus, « 99+ » au-delà) sur les boutons dont l'adresse est celle de Gmail, Google Chat, Discord, Teams,
/// Slack ou d'un service décrit dans <c>notifications.json</c>. Le suivi se fait dans des pages WebView2 cachées qui partagent la session du profil de StreamDak
/// (connecte-toi une fois dans une page web du deck) : aucun identifiant n'est demandé ni enregistré. Les compteurs sont lus sur événement (changement du titre de la page),
/// pas par relevé périodique. Valeurs publiées : <c>{notify.gmail}</c>, <c>{notify.discord}</c>… (vide : inconnu) et <c>{notify.gmail.activity}</c> (« 1 » : activité sans nombre lisible, vide sinon).
/// </summary>
public sealed class NotificationIntegration : IntegrationBase, IBackgroundAware, IDisposable
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "notify";
    /// <summary>Champ qui limite les services suivis.</summary>
    public const string ServicesField = "services";

    static readonly char[] Separators = { ',', ';', ' ' };
    readonly INotificationWatcher? _watcher;
    bool _subscribed;

    /// <summary>Connecteur sans suivi (utilisé pour lire l'identifiant) : il refuse de se connecter.</summary>
    public NotificationIntegration() { }

    /// <summary>Connecteur branché sur le suivi de l'application.</summary>
    public NotificationIntegration(INotificationWatcher watcher) { _watcher = watcher; }

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "Notifications (badges)";
    /// <inheritdoc/>
    public override string Category => "Notifications";
    /// <inheritdoc/>
    public override string Icon => "Alert24";
    /// <inheritdoc/>
    public override string Help =>
        "Affiche un badge rouge avec le nombre de non lus sur les boutons « site web » de Gmail, Google Chat, Discord, Teams et Slack. " +
        "Connecte-toi une fois au service dans une page web du deck : la session est partagée, aucun mot de passe n'est enregistré. " +
        "Chaque service suivi garde une page cachée ouverte (de la mémoire en plus). D'autres services se décrivent dans notifications.json.";

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField(ServicesField, "Services suivis", SettingKind.Text, null, "Identifiants séparés par une virgule (gmail, gchat, discord, teams, slack) ; vide : tous ceux qui ont un bouton."),
    };

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("refresh", "Actualiser les badges", "Relit tout de suite le nombre de notifications de chaque service suivi.", null, false, null, "ArrowSync24", "#B91C1C"),
    };

    /// <inheritdoc/>
    protected override Task ConnectCoreAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        var watcher = _watcher ?? throw new InvalidOperationException("Le suivi des notifications n'est pas disponible");
        if (!_subscribed) { watcher.CountChanged += OnCount; watcher.ActivityChanged += OnActivity; _subscribed = true; }
        var only = settings.Get(ServicesField).Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant()).ToHashSet();
        watcher.Start(only);
        return Task.CompletedTask;
    }

    void OnCount(string provider, int? count) => SetValue(provider, count?.ToString(CultureInfo.InvariantCulture) ?? "");

    /// <summary>Clé de la valeur d'activité d'un service (« gmail.activity ») : un identifiant de service ne contient jamais de point.</summary>
    public static string ActivityKey(string provider) => provider + ".activity";

    void OnActivity(string provider, bool activity) => SetValue(ActivityKey(provider), activity ? "1" : "");

    /// <inheritdoc/>
    protected override Task DisconnectCoreAsync()
    {
        if (_watcher != null)
        {
            if (_subscribed) { _watcher.CountChanged -= OnCount; _watcher.ActivityChanged -= OnActivity; _subscribed = false; }
            _watcher.Stop();
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        if (command != "refresh") throw new ArgumentException($"Commande inconnue : « {command} »");
        (_watcher ?? throw new InvalidOperationException("Le suivi des notifications n'est pas disponible")).Refresh();
        return Task.FromResult<string?>("Badges actualisés");
    }

    /// <inheritdoc/>
    public void SetBackground(bool background) => _watcher?.SetBackground(background);

    /// <summary>Arrête le suivi.</summary>
    public void Dispose()
    {
        DisconnectCoreAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
