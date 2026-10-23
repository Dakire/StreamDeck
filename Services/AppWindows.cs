using System;
using System.Windows;

namespace StreamDak;

/// <summary>Ouverture des paramètres et entrée dans l'édition : l'édition n'a plus de fenêtre, elle se fait dans le deck.</summary>
public interface IAppWindows
{
    /// <summary>
    /// Passe le deck en mode édition : un panneau d'actions à gauche et un panneau de propriétés à droite, autour de la grille, sans autre
    /// fenêtre. Le deck est affiché au besoin.
    /// </summary>
    /// <param name="place">Grille à éditer, ou null pour garder celle qui est affichée.</param>
    /// <param name="index">Case visée, ou -1.</param>
    void OpenEditor(Place? place = null, int index = -1);

    /// <summary>Ouvre les paramètres (ou les ramène devant).</summary>
    void OpenSettings();
}

/// <summary>Fenêtres WPF construites par le conteneur : les paramètres reçoivent leur ViewModel et leurs services par injection.</summary>
public sealed class AppWindows : IAppWindows
{
    readonly IServiceProvider _services;
    SettingsWindow? _settings;

    /// <summary>Gestionnaire de fenêtres ; <paramref name="services"/> sert à construire les paramètres à la demande.</summary>
    public AppWindows(IServiceProvider services) { _services = services; }

    /// <inheritdoc/>
    public void OpenEditor(Place? place = null, int index = -1)
    {
        if (Application.Current.MainWindow is not MainWindow deck) return;
        if (!deck.IsVisible) deck.Show();
        if (deck.WindowState == WindowState.Minimized) deck.WindowState = WindowState.Normal;
        deck.EditCell(place, index);
    }

    /// <inheritdoc/>
    public void OpenSettings()
    {
        if (_settings == null)
        {
            _settings = new SettingsWindow(_services);
            _settings.Closed += (_, _) => _settings = null;
            if (Application.Current.MainWindow is { IsVisible: true } owner) _settings.Owner = owner;
            _settings.Show();
        }
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }
}
