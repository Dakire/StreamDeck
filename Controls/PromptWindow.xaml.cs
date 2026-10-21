using System.Windows;
using System.Windows.Controls;

namespace TouchDeck.Controls;

/// <summary>Petite fenêtre modale : saisie d'un texte, confirmation ou information. Entrée valide, Échap annule.</summary>
public partial class PromptWindow : Wpf.Ui.Controls.FluentWindow
{
    enum Mode { Input, Confirm, Info }

    readonly Mode _mode;
    readonly bool _allowEmpty;

    /// <summary>Texte saisi, si la fenêtre a été validée.</summary>
    public string? Value { get; private set; }

    PromptWindow(Window? owner, Mode mode, string title, string message, string initial, string confirmText, bool allowEmpty, bool danger)
    {
        InitializeComponent();
        if (owner is { IsVisible: true }) Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Closed += (_, _) => WindowCleanup.ReleaseTitleBars(this);
        _mode = mode;
        Title = title;
        Bar.Title = title;
        Message.Text = message;
        OkButton.Content = confirmText;
        Input.Text = initial;
        _allowEmpty = allowEmpty;
        Input.Visibility = mode == Mode.Input ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = mode == Mode.Info ? Visibility.Collapsed : Visibility.Visible;
        if (danger) OkButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Danger;
        Loaded += (_, _) =>
        {
            if (mode == Mode.Input) { Input.Focus(); Input.SelectAll(); }
            else OkButton.Focus();
        };
        UpdateState();
    }

    /// <summary>Demande un texte. Retourne null si l'utilisateur annule.</summary>
    public static string? Ask(Window? owner, string title, string message, string initial, string confirmText = "OK", bool allowEmpty = false)
    {
        var w = new PromptWindow(owner, Mode.Input, title, message, initial, confirmText, allowEmpty, false);
        return w.ShowDialog() == true ? w.Value : null;
    }

    /// <summary>Demande une confirmation ; true si l'utilisateur accepte. <paramref name="danger"/> : bouton de confirmation rouge.</summary>
    public static bool Confirm(Window? owner, string title, string message, string confirmText, bool danger = false) =>
        new PromptWindow(owner, Mode.Confirm, title, message, "", confirmText, true, danger).ShowDialog() == true;

    /// <summary>Affiche une information avec un seul bouton.</summary>
    public static void Info(Window? owner, string title, string message) =>
        new PromptWindow(owner, Mode.Info, title, message, "", "Fermer", true, false).ShowDialog();

    void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateState();

    void UpdateState()
    {
        bool valid = _mode != Mode.Input || _allowEmpty || !string.IsNullOrWhiteSpace(Input.Text);
        OkButton.IsEnabled = valid;
        Error.Text = valid ? "" : "Le nom ne peut pas être vide";
        Error.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
    }

    void OnOk(object sender, RoutedEventArgs e)
    {
        Value = Input.Text.Trim();
        DialogResult = true;
    }
}
