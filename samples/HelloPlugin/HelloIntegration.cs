using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Integrations;

namespace HelloPlugin;

/// <summary>
/// Plugin d'exemple : une intégration sans réglage dont la commande « greet » compte les salutations. Elle montre le minimum à écrire :
/// l'identifiant, les commandes, les valeurs publiées pour les titres (<c>{hello.count}</c>) et la connexion.
/// </summary>
public sealed class HelloIntegration : IntegrationBase
{
    int _count;

    /// <inheritdoc/>
    public override string Id => "hello";
    /// <inheritdoc/>
    public override string Name => "Bonjour (exemple)";
    /// <inheritdoc/>
    public override string Category => "Bonjour (exemple)";
    /// <inheritdoc/>
    public override string Help => "Plugin d'exemple : la commande « Saluer » compte les salutations ; affiche-les avec {hello.count} dans un titre.";
    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = Array.Empty<SettingField>();

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("greet", "Saluer", "Ajoute une salutation au compteur.", "le nom à saluer (facultatif)", false),
    };

    /// <inheritdoc/>
    protected override Task ConnectCoreAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        SetValue("count", _count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task DisconnectCoreAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public override Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        if (command != "greet") throw new ArgumentException($"Commande inconnue : « {command} »");
        SetValue("count", (++_count).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Task.FromResult<string?>($"Bonjour {(string.IsNullOrWhiteSpace(argument) ? "tout le monde" : argument.Trim())} !");
    }
}
