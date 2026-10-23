using System;
using System.Windows;
using StreamDak.Actions;

namespace StreamDak;

/// <summary>Interface vue par l'API locale : chaque appel est ramené sur le thread d'interface, l'API répond depuis un autre thread.</summary>
public sealed class ApiShell : IApiShell
{
    readonly IAppWindows _windows;
    readonly UiNavigator _navigator = new();

    /// <summary>Façade sur le deck affiché et les fenêtres de l'application.</summary>
    public ApiShell(IAppWindows windows) { _windows = windows; }

    /// <inheritdoc/>
    public INavigator Navigator => _navigator;

    /// <inheritdoc/>
    public void ShowDeck() => OnUi(() =>
    {
        if (Application.Current.MainWindow is not { } main) return;
        if (!main.IsVisible) main.Show();
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;   // sans Activate : le deck ne prend jamais le focus
    });

    /// <inheritdoc/>
    public void OpenEditor() => OnUi(() => _windows.OpenEditor());

    /// <inheritdoc/>
    public void OpenSettings() => OnUi(_windows.OpenSettings);

    static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    sealed class UiNavigator : INavigator
    {
        static INavigator? Deck => (Application.Current?.MainWindow as MainWindow)?.Navigator;

        public void GoToPage(string name) => OnUi(() => Deck?.GoToPage(name));
        public void SwitchProfile(string name) => OnUi(() => Deck?.SwitchProfile(name));
    }
}
