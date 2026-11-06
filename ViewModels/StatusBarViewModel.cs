using System;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StreamDak.ViewModels;

/// <summary>
/// Barre d'état du deck : un message à la fois, avec un bouton optionnel (« Annuler » après une création).
/// Une information s'efface seule ; une erreur reste jusqu'à « Fermer » ou jusqu'au message suivant (WCAG 2.2.1).
/// À utiliser depuis le thread d'interface.
/// </summary>
public sealed partial class StatusBarViewModel : ObservableObject
{
    static readonly TimeSpan InfoDelay = TimeSpan.FromSeconds(5);
    static readonly TimeSpan ActionDelay = TimeSpan.FromSeconds(8);

    readonly Action<string, bool> _announce;
    readonly DispatcherTimer _timer = new() { Interval = InfoDelay };
    Action? _action;

    /// <summary><paramref name="announce"/> transmet le texte (et son urgence) aux lecteurs d'écran.</summary>
    public StatusBarViewModel(Action<string, bool> announce)
    {
        _announce = announce;
        _timer.Tick += (_, _) => { _timer.Stop(); Set(null); };
    }

    /// <summary>Message affiché, ou null.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] string? status;
    /// <summary>Texte du bouton proposé, ou null.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatusAction))] string? statusActionText;
    /// <summary>true pour une erreur ou un avertissement.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatusButtons))] bool statusIsError = true;

    /// <summary>true si un message est affiché.</summary>
    public bool HasStatus => Status != null;
    /// <summary>true si le message propose un bouton.</summary>
    public bool HasStatusAction => StatusActionText != null;
    /// <summary>true si la barre porte au moins un bouton touchable : l'action proposée, ou « Fermer » (une erreur reste affichée jusqu'à ce qu'on la ferme).</summary>
    public bool HasStatusButtons => HasStatusAction || StatusIsError;

    /// <summary>Affiche <paramref name="text"/> (null : efface). <paramref name="action"/> : bouton touchable, qui laisse plus de temps pour l'atteindre.</summary>
    public void Set(string? text, bool error = true, string? actionText = null, Action? action = null)
    {
        StatusIsError = error;
        _action = text == null ? null : action;
        StatusActionText = _action == null ? null : actionText;
        Status = text;
        _timer.Stop();
        _timer.Interval = _action == null ? InfoDelay : ActionDelay;
        if (text != null && !error) _timer.Start();
        OnPropertyChanged(nameof(HasStatusButtons));
        if (text != null) _announce(text, error);
    }

    /// <summary>Ferme le message.</summary>
    [RelayCommand]
    void DismissStatus() => Set(null);

    /// <summary>Ferme le message puis exécute l'action proposée.</summary>
    [RelayCommand]
    void RunStatusAction()
    {
        var action = _action;
        Set(null);
        action?.Invoke();
    }
}
