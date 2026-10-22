using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TouchDeck.ViewModels;

namespace TouchDeck;

/// <summary>
/// Mode édition du deck : les panneaux d'actions et de propriétés s'affichent autour de la grille. Ils contiennent des champs de saisie,
/// donc pendant l'édition seulement la fenêtre devient activable et le toucher redevient celui de WPF ; en sortant, WS_EX_NOACTIVATE et
/// le filtre WM_MOUSEACTIVATE reprennent, et le focus retourne à l'application qui l'avait (les raccourcis lui parviennent de nouveau).
/// </summary>
public partial class MainWindow
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);

    /// <summary>true pendant le mode édition : plus de filtre d'activation ni d'interception du toucher.</summary>
    static volatile bool _editing;
    bool _isEditing;
    IntPtr _previousForeground;
    IEditorShell? _editShell;

    /// <inheritdoc/>
    public IEditorShell EditorShell => _editShell ??= new EditShell(this);

    /// <summary>true si la fenêtre est en mode édition (activable).</summary>
    internal static bool IsEditing => _editing;

    /// <inheritdoc/>
    public void SetEditing(bool editing)
    {
        if (_isEditing == editing) return;
        _isEditing = editing;
        _editing = editing;
        ResetTouchTracking();   // le relâchement du doigt qui a ouvert ou fermé l'édition n'arrive plus ici : sans cela le doigt resterait « posé » et plus aucun appui ne passerait
        var hwnd = Hwnd;
        if (hwnd == IntPtr.Zero) return;   // fenêtre pas encore créée (tests hors écran) : le drapeau suffit
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        if (editing)
        {
            var foreground = GetForegroundWindow();
            _previousForeground = foreground != hwnd ? foreground : IntPtr.Zero;
            _ = SetWindowLong(hwnd, GWL_EXSTYLE, style & ~WS_EX_NOACTIVATE);
            if (IsVisible) Activate();   // les panneaux reçoivent le clavier
        }
        else
        {
            _ = SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE);
            if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_NOACTIVATE) == 0) Log.Error("Style WS_EX_NOACTIVATE non rétabli à la fin de l'édition");
            // Le focus revient à l'application qui l'avait : sans cela, les raccourcis suivants partiraient vers le deck
            if (_previousForeground != IntPtr.Zero && IsWindow(_previousForeground)) SetForegroundWindow(_previousForeground);
            _previousForeground = IntPtr.Zero;
        }
    }

    /// <summary>Passe en mode édition sur une case (menu « Modifier… », échec d'un bouton, création qui demande un réglage) ; sans grille, celle qui est affichée.</summary>
    internal void EditCell(Place? place, int index) => _vm.StartEditing(place ?? _vm.CurrentPlace, index);

    /// <summary>Ctrl+Z / Ctrl+Y en mode édition (le deck en a le clavier) : hors champ de saisie, pour ne pas voler l'annulation du texte.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!_editing || _vm.Editor is not { } editor || Keyboard.FocusedElement is TextBox || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Key == Key.Z) { editor.UndoCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Y) { editor.RedoCommand.Execute(null); e.Handled = true; }
    }

    /// <summary>Dialogues (affichés dans le deck) et focus des panneaux intégrés ; seuls les sélecteurs de fichiers de Windows restent des fenêtres.</summary>
    sealed class EditShell : IEditorShell
    {
        readonly MainWindow _w;
        public EditShell(MainWindow window) { _w = window; }

        public Task<string?> PromptAsync(string title, string message, string initial, string confirmText = "OK", bool allowEmpty = false) =>
            _w._vm.AskAsync(title, message, initial, confirmText, allowEmpty);

        public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false) =>
            _w._vm.ConfirmAsync(title, message, confirmText, danger);

        public Task InfoAsync(string title, string message) => _w._vm.InfoAsync(title, message);

        public string? PickOpenFile(string title, string filter)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
            return dialog.ShowDialog(_w) == true ? dialog.FileName : null;
        }

        public string? PickSaveFile(string title, string filter, string defaultName)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = filter, FileName = defaultName, OverwritePrompt = true };
            return dialog.ShowDialog(_w) == true ? dialog.FileName : null;
        }

        void Later(Action action) => _w.Dispatcher.BeginInvoke(DispatcherPriority.Background, action);

        public void FocusProperties() => Later(() => _w.DeckProps.FocusFirstField());
        public void FocusTitle() => Later(() => { if (_w.DeckProps.TitleBox.IsVisible) { _w.DeckProps.TitleBox.Focus(); _w.DeckProps.TitleBox.SelectAll(); } });
        public void FocusIcon() => Later(() =>
        {
            _w.DeckProps.AppearanceExpander.IsExpanded = true;
            _w.DeckProps.AppearanceExpander.UpdateLayout();
            if (_w.DeckProps.IconBox.IsVisible) { _w.DeckProps.IconBox.Focus(); _w.DeckProps.IconBox.SelectAll(); _w.DeckProps.IconBox.BringIntoView(); }
        });
        public void FocusCatalogSearch() => Later(() => { _w.DeckCatalog.SearchBox.Focus(); _w.DeckCatalog.SearchBox.SelectAll(); });
        public void FocusSlot(int listIndex) { }   // la grille du deck est tactile : aucune case à focaliser au clavier

        /// <summary>« Fermer » de l'éditeur rapide n'a pas de sens ici : le mode édition se quitte par « Terminer ».</summary>
        public void Close() { }
    }
}
