using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using StreamDak.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace StreamDak.Tests;

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

    [Theory]
    [InlineData(0)]    // bouton à une action
    [InlineData(30)]   // case vide
    public void EveryInteractiveControlOfTheDeckEditPanelsHasAName(int index)
    {
        _host.Reset(ConfigStore.DefaultConfig());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Run(() => win.EditCell(new Place("Principal", 0), index));
            _host.Settle();
            _host.Render(win, $"a11y-edition-deck-{index}", 1600, 900);
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

    [Fact]
    public void IntegrationReorderGripIsANamedFocusableButton()
    {
        _host.Reset(ConfigStore.DefaultConfig());
        var win = _host.Run(() => new SettingsWindow(_host.Services));
        try
        {
            _host.Render(win, "a11y-poignees", 1100, 1400);
            _host.Run(() =>
            {
                var grips = WpfHost.Descendants<Button>((FrameworkElement)win.Content).Where(b => b.Name == "IntegrationGrip").ToList();
                Assert.NotEmpty(grips);
                foreach (var g in grips)
                {
                    var peer = UIElementAutomationPeer.CreatePeerForElement(g)!;
                    Assert.StartsWith("Déplacer ", peer.GetName());
                    Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
                    Assert.Contains("Alt", AutomationProperties.GetHelpText(g));
                    Assert.True(g.Focusable && g.IsTabStop);
                    Assert.True(g.ActualWidth >= 36 && g.ActualHeight >= 36);
                }
            });
        }
        finally { _host.Run(win.Close); }
    }

    // ---------- annonces d'état, erreurs, boîtes de dialogue ----------

    [Fact]
    public void StatusMessagesAreAnnouncedAndErrorsStayUntilClosed()
    {
        _host.Reset(ConfigStore.DefaultConfig());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            var vm = (MainViewModel)_host.Run(() => win.DataContext);
            var heard = new List<(string Text, bool Urgent)>();
            _host.Run(() => vm.Announced += (t, u) => heard.Add((t, u)));
            _host.Run(() => ((StreamDak.Actions.INavigator)vm).GoToPage("Inconnue"));
            _host.Render(win, "a11y-erreur", 1400, 800);
            Assert.Contains(heard, h => h.Text.Contains("Page introuvable") && h.Urgent);
            Assert.True(_host.Run(() => vm.HasStatusButtons));
            Assert.NotNull(_host.Run(() => UIElementAutomationPeer.CreatePeerForElement(win)));   // la fenêtre porte les notifications

            // « Fermer » : cible tactile d'au moins 48 px, nommée, qui efface l'erreur
            var close = _host.Run(() => WpfHost.Descendants<Button>((FrameworkElement)win.Content)
                .Single(b => AutomationProperties.GetName(b) == "Fermer le message"));
            Assert.True(_host.Run(() => close.ActualHeight) >= 48, "Fermer < 48 px");
            _host.Run(() => close.Command.Execute(null));
            Assert.False(_host.Run(() => vm.HasStatus));
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void AccessibleContentExposesANamedPeerWithItsRole()
    {
        var peer = _host.Run(() =>
        {
            var handle = new StreamDak.Controls.AccessibleContent { ControlType = AutomationControlType.Thumb };
            AutomationProperties.SetName(handle, "Redimensionner le widget");
            var p = UIElementAutomationPeer.CreatePeerForElement(handle)!;
            return (p.GetName(), p.GetAutomationControlType(), p.IsControlElement());
        });
        Assert.Equal(("Redimensionner le widget", AutomationControlType.Thumb, true), peer);
    }

    [Fact]
    public void AnnouncementsReachSubscribers()
    {
        _host.Reset(ConfigStore.DefaultConfig());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            var vm = (MainViewModel)_host.Run(() => win.DataContext);
            var heard = new List<(string Text, bool Urgent)>();
            _host.Run(() => vm.Announced += (t, u) => heard.Add((t, u)));
            _host.Run(() => vm.Announce("Action réussie"));
            Assert.Equal(("Action réussie", false), heard.Single());
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void AnOpenDialogIsNamedAnnouncedAndTrapsTheKeyboard()
    {
        _host.Reset(ConfigStore.DefaultConfig());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            var vm = (MainViewModel)_host.Run(() => win.DataContext);
            var heard = new List<string>();
            _host.Run(() => vm.Announced += (t, _) => heard.Add(t));
            _ = _host.Run(() => vm.AskAsync("Renommer la page", "Choisis un nouveau nom.", "A", "Renommer"));
            _host.Render(win, "a11y-dialogue", 1400, 800);
            _host.Run(() =>
            {
                Assert.Equal("Renommer la page", UIElementAutomationPeer.CreatePeerForElement(win.DialogCard)!.GetName());
                Assert.Equal(System.Windows.Input.KeyboardNavigationMode.Cycle, System.Windows.Input.KeyboardNavigation.GetTabNavigation(win.DialogCard));
                Assert.False(win.MainArea.IsEnabled);                      // panneaux d'édition et grille hors d'atteinte
                Assert.False(win.Dock.IsEnabled);
            });
            _host.Run(() => vm.Dialog!.CancelCommand.Execute(null));
            _host.Settle();
            Assert.True(_host.Run(() => win.MainArea.IsEnabled && win.Dock.IsEnabled));
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void DeckRowsTakeKeyboardFocusOnlyInEditMode()
    {
        _host.Reset(ConfigStore.DefaultConfig());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Render(win, "a11y-focus-deck", 1400, 800);
            List<Button> Buttons() => _host.Run(() => WpfHost.Descendants<Button>(win.DeckGrid).Concat(WpfHost.Descendants<Button>(win.Dock)).ToList());
            Assert.NotEmpty(Buttons());
            Assert.All(Buttons(), b => Assert.False(_host.Run(() => b.Focusable), "focusable hors édition"));   // le deck ne prend jamais le focus

            _host.Run(() => win.EditCell(new Place("Principal", 0), 0));
            _host.Settle();
            _host.Render(win, "a11y-focus-edition", 1600, 900);
            Assert.All(Buttons(), b => Assert.True(_host.Run(() => b.Focusable), "non focusable en édition : " + _host.Run(() => AutomationProperties.GetName(b))));
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void MenuSheetClosesOnEscapeAndHasAName()
    {
        _host.Reset(ConfigStore.DefaultConfig());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            var vm = (MainViewModel)_host.Run(() => win.DataContext);
            _host.Run(() => vm.OpenMenuCommand.Execute(null));
            _host.Render(win, "a11y-menu", 1400, 800);
            _host.Run(() =>
            {
                var escape = win.MenuSheet.InputBindings.OfType<System.Windows.Input.KeyBinding>().Single(k => k.Key == System.Windows.Input.Key.Escape);
                Assert.Equal("Menu", AutomationProperties.GetName(win.MenuSheet));
                Assert.True(vm.MenuOpen);
                escape.Command.Execute(null);
                Assert.False(vm.MenuOpen);
            });
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void CurrentPageTabIsAnnounced()
    {
        _host.Reset(ConfigStore.DefaultConfig());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            var vm = (MainViewModel)_host.Run(() => win.DataContext);
            _host.Render(win, "a11y-onglets", 1400, 800);
            var names = _host.Run(() => vm.Tabs.Select(t => t.AutomationName).ToList());
            Assert.Single(names, n => n.EndsWith(", page courante", System.StringComparison.Ordinal));
        }
        finally { _host.Run(win.Close); }
    }

    // ---------- contraste élevé ----------

    [Theory]
    [InlineData("#FFFFFF", "#000000", "#1AEBFF", "#000000", "#3FF23F", "#FFFF00")]   // contraste élevé noir de Windows
    [InlineData("#000000", "#FFFFFF", "#37006E", "#FFFFFF", "#600000", "#00009F")]   // contraste élevé blanc de Windows
    public void HighContrastPaletteIsReadable(string text, string window, string highlight, string highlightText, string gray, string hot)
    {
        var p = Theme.BuildHighContrast(text, window, highlight, highlightText, gray, hot);
        foreach (var bg in new[] { p.Window, p.Panel, p.Surface, p.SurfaceHi, p.StatusBg })
        {
            Assert.True(Theme.Contrast(p.Text, bg) >= 7, $"Text sur {bg}");
            Assert.True(Theme.Contrast(p.Dim, bg) >= 7, $"Dim sur {bg}");
            Assert.True(Theme.Contrast(p.AccentText, bg) >= 4.5, $"AccentText sur {bg}");
            Assert.True(Theme.Contrast(p.Placeholder, bg) >= 3, $"GrayText sur {bg}");
            Assert.True(Theme.Contrast(p.Focus, bg) >= 3, $"focus sur {bg}");
        }
        Assert.True(Theme.Contrast(p.OnAccent, p.Accent) >= 4.5, "texte sur sélection");
        Assert.True(Theme.Contrast(p.OnAccentContainer, p.AccentContainer) >= 4.5);
        Assert.True(Theme.Contrast(p.OnBadge, p.Badge) >= 4.5, "pastille");
        Assert.True(Theme.Contrast(p.StatusText, p.StatusBg) >= 7, "message d'état");
    }

    [Fact]
    public void HighContrastPublishesSystemColoursAndLeavesCleanly()
    {
        _host.Reset(ConfigStore.DefaultConfig());
        try
        {
            _host.Run(() =>
            {
                Theme.ForcedHighContrast = Theme.BuildHighContrast("#FFFFFF", "#000000", "#1AEBFF", "#000000", "#3FF23F", "#FFFF00");
                Theme.Apply("light");                                        // le réglage du thème est ignoré en contraste élevé
                var r = Application.Current.Resources;
                Assert.True(Theme.HighContrast);
                Assert.Equal(System.Windows.Media.Color.FromRgb(255, 255, 255), ((System.Windows.Media.SolidColorBrush)r["DeckText"]).Color);
                Assert.Equal(System.Windows.Media.Color.FromRgb(0, 0, 0), ((System.Windows.Media.SolidColorBrush)r["DeckWindow"]).Color);
                Assert.Equal(System.Windows.Media.Color.FromRgb(0x1A, 0xEB, 0xFF), ((System.Windows.Media.SolidColorBrush)r["DeckAccent"]).Color);
                Assert.True(Theme.Dark);
            });
        }
        finally
        {
            _host.Run(() => { Theme.ForcedHighContrast = null; Theme.Apply("dark"); Assert.False(Theme.HighContrast); });
        }
    }

    // ---------- avertissement de contraste d'un bouton ----------

    [Theory]
    [InlineData("#FFFFFF", "#6B6E72", true)]    // gris sur le corps de la touche
    [InlineData("#334155", "#1E293B", true)]    // gris foncé sur gris foncé
    [InlineData("#334155", "#FFFFFF", false)]   // blanc sur ardoise
    [InlineData("#334155", null, false)]        // texte par défaut : géré par le thème
    public void FreeTextColourBelowAaIsFlagged(string background, string? text, bool flagged)
    {
        var button = new DeckButton { Color = background, TextColor = text };
        Assert.Equal(flagged, PropertiesViewModel.TextContrastWarning(button) != null);
    }
}
