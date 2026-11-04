using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace StreamDak.Controls;

/// <summary>
/// Conteneur de contenu qui expose un peer UI Automation (un <c>ContentControl</c> ou un <c>Border</c> n'en ont pas : leur
/// <c>AutomationProperties.Name</c> n'atteint jamais un lecteur d'écran). Sert de boîte de dialogue nommée et de poignée de redimensionnement.
/// </summary>
public class AccessibleContent : ContentControl
{
    /// <summary>Rôle UI Automation annoncé (par défaut « Custom »).</summary>
    public AutomationControlType ControlType { get; set; } = AutomationControlType.Custom;

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    sealed class Peer(AccessibleContent owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => owner.ControlType;
        protected override string GetClassNameCore() => nameof(AccessibleContent);
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
    }
}
