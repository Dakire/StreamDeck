using System;
using System.Windows;

namespace TouchDeck;

/// <summary>Ouverture des fenêtres secondaires : une seule instance de l'éditeur et des paramètres à la fois.</summary>
public interface IAppWindows
{
    /// <summary>true si l'éditeur est ouvert (le deck laisse alors l'éditeur afficher les confirmations).</summary>
    bool IsEditorOpen { get; }

    /// <summary>
    /// Ouvre l'éditeur (ou le ramène devant), éventuellement sur un bouton précis. <paramref name="quick"/> : ouverture directe depuis le deck
    /// (clic droit, appui long) pour modifier ou créer ce seul bouton ; la fenêtre propose alors « Enregistrer » et « Annuler » qui la referment.
    /// </summary>
    void OpenEditor(Place? place = null, int index = -1, bool quick = false);

    /// <summary>Ouvre les paramètres (ou les ramène devant), au-dessus de l'éditeur s'il est ouvert.</summary>
    void OpenSettings();
}

/// <summary>Fenêtres WPF construites par le conteneur : chacune reçoit son ViewModel et ses services par injection.</summary>
public sealed class AppWindows : IAppWindows
{
    readonly IServiceProvider _services;
    EditorWindow? _editor;
    SettingsWindow? _settings;

    /// <summary>Gestionnaire de fenêtres ; <paramref name="services"/> sert à construire l'éditeur et les paramètres à la demande.</summary>
    public AppWindows(IServiceProvider services) { _services = services; }

    /// <inheritdoc/>
    public bool IsEditorOpen => _editor != null;

    /// <inheritdoc/>
    public void OpenEditor(Place? place = null, int index = -1, bool quick = false)
    {
        if (_editor == null)
        {
            _editor = new EditorWindow(_services);
            _editor.Closed += (_, _) => _editor = null;
            if (Application.Current.MainWindow is { IsVisible: true } main) _editor.Owner = main;
            _editor.Show();
        }
        _editor.GoTo(place, index, quick);
        if (_editor.WindowState == WindowState.Minimized) _editor.WindowState = WindowState.Normal;
        _editor.Activate();
    }

    /// <inheritdoc/>
    public void OpenSettings()
    {
        if (_settings == null)
        {
            _settings = new SettingsWindow(_services);
            _settings.Closed += (_, _) => _settings = null;
            Window? owner = _editor ?? Application.Current.MainWindow;
            if (owner is { IsVisible: true }) _settings.Owner = owner;
            _settings.Show();
        }
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }
}
