using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;

namespace TouchDeck;

/// <summary>
/// Icône de la zone de notification : retrouver le deck, l'éditeur et les paramètres quand la fenêtre est cachée ou sur un autre écran,
/// et quitter proprement. Créée par l'application seulement (jamais dans les tests, qui ne doivent rien afficher).
/// </summary>
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;
    readonly ContextMenuStrip _menu = new();

    /// <summary>Icône dont les entrées passent par <paramref name="shell"/> (même chemin que l'API locale).</summary>
    public TrayIcon(IApiShell shell)
    {
        _menu.Items.Add("Afficher le deck", null, (_, _) => shell.ShowDeck());
        _menu.Items.Add("Modifier le deck", null, (_, _) => shell.OpenEditor());
        _menu.Items.Add("Paramètres", null, (_, _) => shell.OpenSettings());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Quitter TouchDeck", null, (_, _) => System.Windows.Application.Current.Shutdown());
        _icon = new NotifyIcon
        {
            Text = "TouchDeck",
            Icon = LoadIcon(),
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => shell.ShowDeck();
    }

    static Icon LoadIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"));
            if (info != null) { using var stream = info.Stream; return new Icon(stream); }
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or ArgumentException) { Log.Warn($"Icône de la zone de notification : {ex.Message}"); }
        return SystemIcons.Application;
    }

    /// <summary>Retire l'icône (sinon elle reste affichée jusqu'au survol de la souris).</summary>
    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
