using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Integrations;

/// <summary>État de la connexion d'une intégration avec le logiciel ou le service qu'elle pilote.</summary>
public enum ConnectionState
{
    /// <summary>Désactivée dans les paramètres (état déduit de la configuration par l'interface : l'intégration, elle, reste « déconnectée »).</summary>
    Disabled,
    /// <summary>Activée, pas encore connectée (logiciel absent, reconnexion programmée).</summary>
    Disconnected,
    /// <summary>Connexion en cours.</summary>
    Connecting,
    /// <summary>Connectée : les commandes sont exécutées et les états sont à jour.</summary>
    Connected,
    /// <summary>Dernière tentative en échec ; <see cref="IIntegration.Error"/> dit pourquoi.</summary>
    Error,
}

/// <summary>Nature d'un réglage d'intégration, qui décide de son champ dans les paramètres.</summary>
public enum SettingKind
{
    /// <summary>Texte libre (adresse, nom).</summary>
    Text,
    /// <summary>Nombre (port).</summary>
    Number,
    /// <summary>Secret (mot de passe, jeton) : jamais écrit dans config.json ni dans le journal.</summary>
    Secret,
}

/// <summary>Un réglage que l'utilisateur renseigne pour une intégration.</summary>
/// <param name="Key">Clé stable (« host », « port », « password »…).</param>
/// <param name="Label">Libellé affiché.</param>
/// <param name="Kind">Champ de texte, nombre ou secret.</param>
/// <param name="Default">Valeur par défaut proposée.</param>
/// <param name="Hint">Aide affichée sous le champ.</param>
public sealed record SettingField(string Key, string Label, SettingKind Kind, string? Default = null, string? Hint = null);

/// <summary>Une commande qu'une intégration expose : elle devient une action du catalogue et une étape d'un bouton.</summary>
/// <param name="Name">Identifiant stable, écrit dans <c>DeckAction.Value</c> (« scene », « mute »…).</param>
/// <param name="Label">Nom affiché dans le catalogue et l'éditeur.</param>
/// <param name="Description">Ce que fait la commande.</param>
/// <param name="ArgHint">Ce qu'attend <c>DeckAction.Args</c> (nom de scène, de source…) ; null si la commande n'a pas d'argument.</param>
/// <param name="ArgRequired">true si l'argument est obligatoire.</param>
/// <param name="StateKey">Clé de la valeur d'état qui dit si la commande est « active » (voyant ON / OFF de la touche) ; null sinon.
/// « {arg} » est remplacé par l'argument de la commande.</param>
/// <param name="Icon">Icône Fluent du bouton créé.</param>
/// <param name="Color">Couleur du bouton créé.</param>
public sealed record IntegrationCommand(string Name, string Label, string Description, string? ArgHint = null, bool ArgRequired = false,
    string? StateKey = null, string Icon = "PlugConnected24", string Color = "#334155");

/// <summary>
/// Un logiciel ou un service que StreamDak sait piloter. Une intégration expose ses <see cref="Commands"/> (actions), ses
/// <see cref="Values"/> (états lisibles : titres dynamiques, voyants) et gère sa propre connexion. Une intégration qui échoue
/// ne doit jamais bloquer l'interface ni les autres : elle lève une exception, le <see cref="IntegrationManager"/> la consigne
/// et retente avec une attente croissante.
/// </summary>
public interface IIntegration
{
    /// <summary>Identifiant stable : clé de <c>Integrations</c> dans config.json et type d'action (« obs », « vmix »…).</summary>
    string Id { get; }
    /// <summary>Nom affiché.</summary>
    string Name { get; }
    /// <summary>Catégorie du catalogue d'actions.</summary>
    string Category { get; }
    /// <summary>Icône Fluent de l'intégration.</summary>
    string Icon { get; }
    /// <summary>Prérequis et mode d'emploi, affichés dans les paramètres (activer l'API du logiciel, etc.).</summary>
    string Help { get; }
    /// <summary>Réglages à renseigner (adresse, port, mot de passe…).</summary>
    IReadOnlyList<SettingField> Fields { get; }
    /// <summary>Commandes exposées.</summary>
    IReadOnlyList<IntegrationCommand> Commands { get; }

    /// <summary>État de la connexion.</summary>
    ConnectionState State { get; }
    /// <summary>Raison du dernier échec, ou null.</summary>
    string? Error { get; }
    /// <summary>L'état de la connexion a changé (peut être levé depuis n'importe quel thread).</summary>
    event Action? StateChanged;

    /// <summary>Valeurs d'état lisibles (« scene », « streaming »…), vides tant que l'intégration n'est pas connectée.</summary>
    IReadOnlyDictionary<string, string> Values { get; }
    /// <summary>Des valeurs ont changé : les clés concernées (peut être levé depuis n'importe quel thread).</summary>
    event Action<IReadOnlyCollection<string>>? ValuesChanged;

    /// <summary>Se connecte avec ces réglages et lit l'état initial. Lève une exception, avec un message affichable, en cas d'échec.</summary>
    Task ConnectAsync(IntegrationSettings settings, CancellationToken cancellation);
    /// <summary>Se déconnecte proprement et efface l'erreur (désactivation par l'utilisateur). Ne lève jamais d'exception.</summary>
    Task DisconnectAsync();
    /// <summary>Libère la connexion sans toucher à l'état affiché (avant une nouvelle tentative : l'erreur reste lisible). Ne lève jamais d'exception.</summary>
    Task ReleaseAsync();
    /// <summary>Exécute une commande ; retourne un message à afficher ou null. Lève une exception, avec un message affichable, en cas d'échec.</summary>
    Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation);
}

/// <summary>Réglages d'une intégration, lus dans la configuration et dans le magasin de secrets.</summary>
public sealed class IntegrationSettings
{
    /// <summary>Réglages non secrets (clé du champ → valeur).</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    /// <summary>Secrets (clé du champ → valeur), jamais écrits ailleurs que dans le magasin de secrets.</summary>
    public IReadOnlyDictionary<string, string> Secrets { get; init; } = new Dictionary<string, string>();

    /// <summary>Valeur d'un réglage, ou <paramref name="fallback"/> s'il est absent ou vide.</summary>
    public string Get(string key, string fallback = "") =>
        Values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    /// <summary>Réglage numérique, ou <paramref name="fallback"/> s'il est absent ou invalide.</summary>
    public int GetInt(string key, int fallback) =>
        int.TryParse(Get(key), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) && n is > 0 and < 65536 ? n : fallback;

    /// <summary>Secret, ou chaîne vide s'il n'est pas renseigné.</summary>
    public string Secret(string key) => Secrets.TryGetValue(key, out var v) ? v : "";
}

/// <summary>Magasin de secrets (mots de passe, jetons) : chiffrés pour l'utilisateur courant, jamais dans config.json ni dans le journal.</summary>
public interface ISecretStore
{
    /// <summary>Secret enregistré, ou null.</summary>
    string? Get(string key);
    /// <summary>Enregistre un secret ; une valeur vide le supprime.</summary>
    void Set(string key, string? value);
}

/// <summary>
/// Intégration qui sait ralentir quand personne ne regarde le deck (fenêtre réduite ou masquée) : un relevé toutes les secondes
/// pour un affichage invisible réveille le processeur pour rien. Le gestionnaire le signale à chaque changement.
/// </summary>
public interface IBackgroundAware
{
    /// <summary>true : le deck n'est pas visible, espacer ou suspendre les relevés ; false : reprendre tout de suite au rythme normal.</summary>
    void SetBackground(bool background);
}
