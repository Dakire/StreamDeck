using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TouchDeck.Controls;
using TouchDeck.ViewModels;

namespace TouchDeck;

/// <summary>
/// Fenêtre de l'éditeur. La logique vit dans <see cref="EditorViewModel"/> ; ici : dialogues, focus, raccourcis clavier,
/// menus contextuels et glisser-déposer (ce qui relève des événements WPF).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Le ViewModel est libéré à la fermeture de la fenêtre (Closed), fin de vie d'une Window WPF.")]
public partial class EditorWindow : Wpf.Ui.Controls.FluentWindow, IEditorShell
{
    const string DragFormat = "TouchDeck.Drag";

    readonly EditorViewModel _vm;

    // glisser-déposer
    DragPayload? _drag;
    Point _dragStart;
    SlotViewModel? _pressedSlot;
    CatalogItemViewModel? _pressedCatalog;
    SlotViewModel? _hoverSlot;
    PageTabViewModel? _hoverPage;
    DragGhost? _ghost;

    /// <summary>Éditeur sur le document partagé ; s'ouvre sur l'écran du pointeur, enregistre à la fermeture.</summary>
    public EditorWindow()
    {
        _vm = new EditorViewModel(this);
        DataContext = _vm;
        InitializeComponent();
        Icon = Application.Current.MainWindow?.Icon;
        Closed += (_, _) => { _vm.Dispose(); AppServices.Flush(); };
        SourceInitialized += (_, _) => PlaceOnCursorScreen();
    }

    /// <summary>Sélectionne un emplacement (profil, page, dossier) et éventuellement une case.</summary>
    public void GoTo(Place? place, int index) => _vm.GoTo(place, index);

    /// <summary>S'ouvre sur l'écran où se trouve le curseur (l'éditeur se pilote à la souris et au clavier, pas sur l'écran tactile du deck).</summary>
    void PlaceOnCursorScreen()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var wa = screen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        double w = Math.Min(Width, wa.Width / dpi.DpiScaleX * 0.96), h = Math.Min(Height, wa.Height / dpi.DpiScaleY * 0.96);
        Width = w; Height = h;
        Left = wa.X / dpi.DpiScaleX + (wa.Width / dpi.DpiScaleX - w) / 2;
        Top = wa.Y / dpi.DpiScaleY + (wa.Height / dpi.DpiScaleY - h) / 2;
    }

    // ---------- IEditorShell : dialogues ----------

    /// <inheritdoc/>
    public Task<string?> PromptAsync(string title, string message, string initial, string confirmText = "OK", bool allowEmpty = false) =>
        Task.FromResult(PromptWindow.Ask(this, title, message, initial, confirmText, allowEmpty));

    /// <inheritdoc/>
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false) =>
        Task.FromResult(PromptWindow.Confirm(this, title, message, confirmText, danger));

    /// <inheritdoc/>
    public Task InfoAsync(string title, string message)
    {
        PromptWindow.Info(this, title, message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public string? PickOpenFile(string title, string filter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    /// <inheritdoc/>
    public string? PickSaveFile(string title, string filter, string defaultName)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = filter, FileName = defaultName, OverwritePrompt = true };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    // ---------- IEditorShell : focus ----------

    void Later(Action a) => Dispatcher.BeginInvoke(DispatcherPriority.Background, a);

    /// <inheritdoc/>
    public void FocusProperties() => Later(() =>
    {
        // Première étape à compléter, sinon le titre
        var invalid = FindVisualChildren<TextBox>(PropertiesScroll)
            .FirstOrDefault(t => t.DataContext is ActionStepViewModel { HasIssues: true } && t.IsVisible && t.IsEnabled && t != TitleBox);
        var target = invalid ?? (TitleBox.IsVisible ? TitleBox : null);
        if (target != null) { target.Focus(); Keyboard.Focus(target); }
        else PropertiesScroll.Focus();
    });

    /// <inheritdoc/>
    public void FocusTitle() => Later(() => { if (TitleBox.IsVisible) { TitleBox.Focus(); TitleBox.SelectAll(); } });
    /// <inheritdoc/>
    public void FocusIcon() => Later(() => { if (IconBox.IsVisible) { IconBox.Focus(); IconBox.SelectAll(); IconBox.BringIntoView(); } });
    /// <inheritdoc/>
    public void FocusCatalogSearch() => Later(() => { CatalogSearch.Focus(); CatalogSearch.SelectAll(); });

    /// <inheritdoc/>
    public void FocusSlot(int listIndex) => Later(() =>
    {
        var slot = listIndex >= 0 ? _vm.Slots.FirstOrDefault(s => !s.IsBack && s.Index == listIndex) : _vm.SelectedSlot ?? _vm.Slots.FirstOrDefault(s => !s.IsBack);
        if (slot == null) return;
        if (SlotList.ItemContainerGenerator.ContainerFromItem(slot) is ListBoxItem item) item.Focus();
    });

    static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var sub in FindVisualChildren<T>(child)) yield return sub;
        }
    }

    // ---------- clavier ----------

    static bool IsTextInput(object? o) => o is TextBoxBase or PasswordBox or ComboBox { IsEditable: true };

    bool InGrid => Keyboard.FocusedElement is ListBoxItem item && ItemsControl.ItemsControlFromItemContainer(item) == SlotList;

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Les raccourcis ne doivent jamais perturber la saisie dans les champs de texte
        if (IsTextInput(Keyboard.FocusedElement) || IsTextInput(e.OriginalSource)) return;
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control), alt = mods.HasFlag(ModifierKeys.Alt), shift = mods.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (ctrl && !alt)
        {
            switch (key)
            {
                case Key.Z: (shift ? _vm.RedoCommand : _vm.UndoCommand).Execute(null); e.Handled = true; return;
                case Key.Y: _vm.RedoCommand.Execute(null); e.Handled = true; return;
                case Key.F: FocusCatalogSearch(); e.Handled = true; return;
                case Key.C when InGrid: _vm.CopyCommand.Execute(null); e.Handled = true; return;
                case Key.X when InGrid: _vm.CutCommand.Execute(null); e.Handled = true; return;
                case Key.V when InGrid: _vm.PasteCommand.Execute(null); e.Handled = true; return;
                case Key.D when InGrid: _vm.DuplicateCommand.Execute(null); e.Handled = true; return;
            }
        }
        if (!InGrid) return;

        if (alt && !ctrl)
        {
            int dx = key == Key.Left ? -1 : key == Key.Right ? 1 : 0, dy = key == Key.Up ? -1 : key == Key.Down ? 1 : 0;
            if (dx != 0 || dy != 0) { _vm.MoveSelection(dx, dy); e.Handled = true; }
            return;
        }
        if (ctrl || alt) return;

        switch (key)
        {
            case Key.Delete: _vm.DeleteSelection(); e.Handled = true; break;
            case Key.F2: _vm.RenameButtonCommand.Execute(null); e.Handled = true; break;
            case Key.Back: if (_vm.InFolder) { _vm.GoUp(); e.Handled = true; } break;
            case Key.Enter: ActivateFocusedSlot(); e.Handled = true; break;
        }
    }

    void ActivateFocusedSlot()
    {
        if (Keyboard.FocusedElement is ListBoxItem { DataContext: SlotViewModel slot })
        {
            if (slot.IsBack) _vm.GoUp();
            else if (slot.IsFolder) _vm.OpenSelectedFolder();
            else if (slot.IsEmpty) FocusCatalogSearch();
            else FocusProperties();
        }
    }

    void CatalogSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _vm.Catalog.IsSearching) { _vm.Catalog.Search = ""; e.Handled = true; }
        else if (e.Key == Key.Down) { CatalogSearch.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; }
    }

    // ---------- menus contextuels ----------

    void Slot_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Le clic droit sélectionne la case (comme Elgato) mais ne déclenche rien d'autre
        if (sender is ListBoxItem { DataContext: SlotViewModel slot } item)
        {
            if (!slot.IsBack) _vm.SelectIndex(slot.Index);
            item.Focus();
        }
    }

    void Slot_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: SlotViewModel slot } item) return;
        if (!slot.IsBack && _vm.SelectedSlot != slot) _vm.SelectIndex(slot.Index);
        item.ContextMenu = ContextMenuFactory.Build(_vm.BuildSlotMenu(slot));
    }

    void ProfileCombo_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_vm.SelectedProfile == null) return;
        ProfileCombo.ContextMenu = ContextMenuFactory.Build(_vm.BuildProfileMenu(_vm.SelectedProfile.Name));
    }

    void ProfileItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) { /* le menu s'ouvre sur l'élément sans changer la sélection */ }

    void ProfileItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ComboBoxItem { DataContext: ProfileItemViewModel p } item) return;
        item.ContextMenu = ContextMenuFactory.Build(_vm.BuildProfileMenu(p.Name));
    }

    void ProfileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProfile != null)
            ContextMenuFactory.ShowBelow(ProfileMenuButton, _vm.BuildProfileMenu(_vm.SelectedProfile.Name));
    }

    void PageTab_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: PageTabViewModel page } item) { item.IsSelected = true; item.Focus(); }
    }

    void PageTab_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: PageTabViewModel page } item)
            item.ContextMenu = ContextMenuFactory.Build(_vm.BuildPageMenu(page));
    }

    async void PageTab_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: PageTabViewModel page }) await _vm.RenamePageAsync(page);
    }

    void CatalogRow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is Border { DataContext: CatalogItemViewModel item } row)
            row.ContextMenu = ContextMenuFactory.Build(_vm.BuildCatalogMenu(item));
    }

    void CatalogRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border row) row.Focus();
    }

    void CatalogRow_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Border { DataContext: CatalogItemViewModel item } || e.OriginalSource != sender) return;
        if (e.Key is Key.Enter or Key.Space) { _vm.AddCatalogAction(item.Action); e.Handled = true; }
    }

    // ---------- glisser-déposer : sources ----------

    static bool IsInsideButton(DependencyObject? d)
    {
        while (d != null) { if (d is ButtonBase) return true; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d); }
        return false;
    }

    void Slot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _pressedSlot = sender is ListBoxItem { DataContext: SlotViewModel { IsBack: false, IsEmpty: false } s } ? s : null;
    }

    void Slot_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedSlot == null || sender is not ListBoxItem item || item.DataContext != _pressedSlot) return;
        if (!ExceededDragThreshold(e)) return;
        var slot = _pressedSlot;
        _pressedSlot = null;
        BeginDrag(item, new DragPayload(_vm.Place, slot.Index, null), slot);
    }

    void CatalogRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _pressedCatalog = null;
        if (IsInsideButton(e.OriginalSource as DependencyObject) || sender is not Border { DataContext: CatalogItemViewModel item } row) return;
        row.Focus();
        if (e.ClickCount == 2) { _vm.AddCatalogAction(item.Action); e.Handled = true; return; }   // double-clic : ajoute à la case sélectionnée ou libre
        _pressedCatalog = item;
    }

    void CatalogRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _pressedCatalog = null;

    void CatalogRow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedCatalog == null || sender is not Border row) return;
        if (!ExceededDragThreshold(e)) return;
        var item = _pressedCatalog;
        _pressedCatalog = null;
        BeginDrag(row, new DragPayload(null, -1, item.Action.Id), null);
    }

    bool ExceededDragThreshold(MouseEventArgs e)
    {
        var d = e.GetPosition(null) - _dragStart;
        return Math.Abs(d.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(d.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    void BeginDrag(FrameworkElement source, DragPayload payload, SlotViewModel? slot)
    {
        _drag = payload;
        if (slot != null) slot.IsDragSource = true;
        var layer = AdornerLayer.GetAdornerLayer(RootGrid);
        if (layer != null)
        {
            _ghost = new DragGhost(RootGrid, source);
            layer.Add(_ghost);
            _ghost.Move(Mouse.GetPosition(RootGrid));
        }
        try
        {
            var data = new DataObject(DragFormat, "1");
            DragDrop.DoDragDrop(source, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            if (_ghost != null) { layer?.Remove(_ghost); _ghost = null; }
            if (slot != null) slot.IsDragSource = false;
            SetHover(null);
            SetHoverPage(null);
            _drag = null;
        }
    }

    void OnWindowPreviewDragOver(object sender, DragEventArgs e) => _ghost?.Move(e.GetPosition(RootGrid));

    // ---------- glisser-déposer : cibles ----------

    void SetHover(SlotViewModel? slot)
    {
        if (_hoverSlot == slot) return;
        if (_hoverSlot != null) _hoverSlot.IsDropTarget = false;
        _hoverSlot = slot;
        if (slot != null) slot.IsDropTarget = true;
    }

    void SetHoverPage(PageTabViewModel? page)
    {
        if (_hoverPage == page) return;
        if (_hoverPage != null) _hoverPage.IsDropTarget = false;
        _hoverPage = page;
        if (page != null) page.IsDropTarget = true;
    }

    static bool CopyRequested(DragEventArgs e) => e.KeyStates.HasFlag(DragDropKeyStates.ControlKey);

    void Slot_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_drag == null || sender is not ListBoxItem { DataContext: SlotViewModel target })
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        var kind = _vm.EvaluateDrop(_drag, target, CopyRequested(e));
        e.Effects = kind == DropKind.None ? DragDropEffects.None : kind == DropKind.Copy ? DragDropEffects.Copy : DragDropEffects.Move;
        SetHover(kind == DropKind.None ? null : target);
    }

    void Slot_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: SlotViewModel target } && _hoverSlot == target) SetHover(null);
    }

    void Slot_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var payload = _drag;
        SetHover(null);
        if (payload == null || sender is not ListBoxItem { DataContext: SlotViewModel target }) return;
        _vm.Drop(payload, target, CopyRequested(e));
        FocusSlot(target.Index);
    }

    void PageTab_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_drag == null || sender is not ListBoxItem { DataContext: PageTabViewModel page } || !_vm.CanDropOnPage(_drag, page))
        {
            e.Effects = DragDropEffects.None;
            SetHoverPage(null);
            return;
        }
        e.Effects = DragDropEffects.Move;
        SetHoverPage(page);
    }

    void PageTab_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: PageTabViewModel page } && _hoverPage == page) SetHoverPage(null);
    }

    void PageTab_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var payload = _drag;
        SetHoverPage(null);
        if (payload != null && sender is ListBoxItem { DataContext: PageTabViewModel page }) _vm.DropOnPage(payload, page);
    }

    void Slot_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: SlotViewModel slot } || e.ChangedButton != MouseButton.Left) return;
        e.Handled = true;
        if (slot.IsBack) _vm.GoUp();
        else if (slot.IsFolder) _vm.OpenSelectedFolder();
        else if (slot.IsEmpty) FocusCatalogSearch();
        else FocusProperties();
    }

    /// <summary>Aperçu de l'élément déplacé : une copie translucide de la case qui suit le curseur.</summary>
    sealed class DragGhost : Adorner
    {
        readonly VisualBrush _brush;
        readonly Size _size;
        Point _pos;

        public DragGhost(UIElement adorned, FrameworkElement source) : base(adorned)
        {
            IsHitTestVisible = false;
            _size = new Size(Math.Max(40, source.ActualWidth), Math.Max(40, source.ActualHeight));
            _brush = new VisualBrush(source) { Opacity = 0.8, Stretch = Stretch.Uniform };
        }

        public void Move(Point p)
        {
            _pos = p;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc) =>
            dc.DrawRoundedRectangle(_brush, null, new Rect(_pos.X - _size.Width / 2, _pos.Y - _size.Height / 2, _size.Width, _size.Height), 14, 14);
    }
}
