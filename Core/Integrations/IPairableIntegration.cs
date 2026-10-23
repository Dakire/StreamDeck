using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Integrations;

/// <summary>
/// Intégration qui s'associe à un appareil par une démarche physique (Philips Hue : appui sur le bouton du pont). Les paramètres
/// affichent alors un bouton « Associer » qui obtient la clé et l'enregistre comme secret.
/// </summary>
public interface IPairableIntegration : IIntegration
{
    /// <summary>Texte du bouton d'association.</summary>
    string PairLabel { get; }
    /// <summary>Marche à suivre avant d'appuyer sur le bouton (« appuie sur le bouton du pont, puis… »).</summary>
    string PairHelp { get; }
    /// <summary>Clé du champ secret qui reçoit le résultat de l'association.</summary>
    string PairedSecretField { get; }
    /// <summary>Associe StreamDak à l'appareil et retourne le secret à enregistrer. Lève une exception, avec un message affichable, en cas d'échec.</summary>
    Task<string> PairAsync(IntegrationSettings settings, CancellationToken cancellation);
}