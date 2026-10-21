using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TouchDeck.Controls;
using TouchDeck.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace TouchDeck.Tests;

/// <summary>
/// Accessibilité : un lecteur d'écran doit pouvoir nommer chaque contrôle interactif. Le nom vient du texte du contrôle ou de
/// <c>AutomationProperties.Name</c> (un contrôle qui n'a qu'une icône doit donc en porter un).
/// </summary>
[Collection("ui")]
public class AccessibilityTests
{
    readonly WpfHost _host;
    readonly ITestOutputHelper _out;
    public AccessibilityTests(WpfHost host, ITestOutputHelper output) { _host = host; _out = output; }

    /// <summary>Contrôles interactifs affichés (taille non nulle) dont le nom d'automatisation est vide.</summary>
    List<string> Unnamed(Window window)
    {
        return _host.Run(() =>
        {
            var found = new List<string>();
            foreach (var c in WpfHost.Descendants<Control>((FrameworkElement)window.Content).Where(IsInteractive))
            {
                // Les pièces de gabarit (flèches de défilement, bouton d'un Expander ou d'une liste déroulante, barre de titre) sont
                // nommées par le contrôle qui les contient, pas individuellement
                if (c.TemplatedParent != null || c.ActualWidth <= 0 || c.ActualHeight <= 0) continue;
                var name = AutomationProperties.GetName(c);
                if (string.IsNullOrWhiteSpace(name)) name = UIElementAutomationPeer.CreatePeerForElement(c)?.GetName();
                if (string.IsNullOrWhiteSpace(name) && AutomationProperties.GetLabeledBy(c) != null) name = "(étiquette)";
                if (string.IsNullOrWhiteSpace(name)) found.Add($"{c.GetType().Name} sous {Path(c)}");
            }
            return found;
        });
    }

    static bool IsInteractive(Control c) => c is ButtonBase or TextBox or ComboBox or Slider or Selector and not TabControl and not ListBox;

    static string Path(DependencyObject c)
    {
        var parts = new List<string>();
        for (var d = System.Windows.Media.VisualTreeHelper.GetParent(c); d != null && parts.Count < 4; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is FrameworkElement { Name: { Length: > 0 } n }) parts.Add(n);
            else if (d is FrameworkElement { DataContext: { } dc } && !parts.Contains(dc.GetType().Name)) parts.Add(dc.GetType().Name);
        return string.Join(" < ", parts);
    }

    [Fact]
    public void EveryInteractiveControlOfTheDeckHasAName()
    {
        _host.Reset(ConfigStore.DefaultConfig());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Render(win, "a11y-deck", 1400, 800);
            var vm = (MainViewModel)_host.Run(() => win.DataContext);
            _host.Run(() => vm.OpenMenuCommand.Execute(null));                       // feuille de menu ouverte
            _host.Render(win, "a11y-deck", 1400, 800);
            var unnamed = Unnamed(win);
            Assert.True(unnamed.Count == 0, string.Join("\n", unnamed));
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void EveryInteractiveControlOfTheQuickPanelHasAName()
    {
        _host.Reset(ConfigStore.DefaultConfig());
        var win = _host.Run(() => new QuickCreateWindow(_host.Services, new Place("Principal", 0), 30));
        try
        {
            _host.Render(win, "a11y-creation-rapide", 540, 780);
            var unnamed = Unnamed(win);
            Assert.True(unnamed.Count == 0, string.Join("\n", unnamed));
        }
        finally { _host.Run(win.Close); }
    }

    [Theory]
    [InlineData(0)]    // bouton à une action
    [InlineData(30)]   // case vide
    public void EveryInteractiveControlOfTheEditorHasAName(int index)
    {
        _host.Reset(ConfigStore.DefaultConfig());
        var win = _host.Run(() => new EditorWindow(_host.Services));
        try
        {
            _host.Run(() => win.GoTo(new Place("Principal", 0), index));
            _host.Settle();
            _host.Render(win, $"a11y-editeur-{index}", 1400, 880);
            var unnamed = Unnamed(win);
            Assert.True(unnamed.Count == 0, string.Join("\n", unnamed));
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void EveryInteractiveControlOfTheSettingsHasAName()
    {
        _host.Reset(ConfigStore.DefaultConfig());
        var win = _host.Run(() => new SettingsWindow(_host.Services));
        try
        {
            _host.Render(win, "a11y-parametres", 1100, 800);
            var unnamed = Unnamed(win);
            Assert.True(unnamed.Count == 0, string.Join("\n", unnamed));
        }
        finally { _host.Run(win.Close); }
    }
}
