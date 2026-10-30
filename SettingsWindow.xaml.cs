using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using StreamDak.Controls;
using StreamDak.ViewModels;

namespace StreamDak;

/// <summary>Fenêtre des paramètres : la logique vit dans <see cref="SettingsViewModel"/>, ici seulement les dialogues.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Le ViewModel est libéré à la fermeture de la fenêtre (Closed), fin de vie d'une Window WPF.")]
public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow, ISettingsShell
{
    readonly SettingsViewModel _vm;

    /// <summary>Paramètres sur le document partagé ; le ViewModel est libéré à la fermeture.</summary>
    public SettingsWindow(IServiceProvider services)
    {
        _vm = ActivatorUtilities.CreateInstance<SettingsViewModel>(services, this);   // la fenêtre est le « shell » du ViewModel
        DataContext = _vm;
        InitializeComponent();
        ShortcutList.ItemsSource = SettingsViewModel.Shortcuts.Select(s => new { s.Keys, s.What }).ToList();
        Icon = Application.Current.MainWindow?.Icon;
        Closed += (_, _) => OnClosed();
    }

    // ---------- ordre des intégrations : glisser la poignée d'une carte sur une autre (ou Alt + flèches) ----------

    const string IntegrationFormat = "StreamDak.Integration";
    IntegrationCardViewModel? _gripSource;
    Point _gripStart;

    static IntegrationCardViewModel? CardOf(object sender) => (sender as FrameworkElement)?.DataContext as IntegrationCardViewModel;

    void OnIntegrationGripDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _gripSource = CardOf(sender);
        _gripStart = e.GetPosition(null);
    }

    void OnIntegrationGripMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_gripSource == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var now = e.GetPosition(null);
        if (Math.Abs(now.X - _gripStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - _gripStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var source = _gripSource;
        _gripSource = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(IntegrationFormat, source), DragDropEffects.Move);
    }

    void OnIntegrationGripKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) == 0 || CardOf(sender) is not { } card) return;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        int delta = key == System.Windows.Input.Key.Up ? -1 : key == System.Windows.Input.Key.Down ? 1 : 0;
        if (delta == 0) return;
        e.Handled = true;
        int at = _vm.Integrations.IndexOf(card);
        _vm.MoveIntegration(at, at + delta);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => ((UIElement)sender).Focus());
    }

    void OnIntegrationDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(IntegrationFormat)) Behaviors.DropState.SetIsOver((DependencyObject)sender, true);
    }

    void OnIntegrationDragLeave(object sender, DragEventArgs e) => Behaviors.DropState.SetIsOver((DependencyObject)sender, false);

    void OnIntegrationDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(IntegrationFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    void OnIntegrationDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        Behaviors.DropState.SetIsOver((DependencyObject)sender, false);
        if (e.Data.GetData(IntegrationFormat) is IntegrationCardViewModel moved && CardOf(sender) is { } target)
            _vm.MoveIntegration(_vm.Integrations.IndexOf(moved), _vm.Integrations.IndexOf(target));
    }

    /// <summary>
    /// Libération à la fermeture, une seule fois, chronométrée étape par étape : une ligne dans le journal, en avertissement au-delà de 100 ms
    /// (la fermeture gelait l'interface 6 s avant la correction du parcours de <see cref="WindowCleanup"/>).
    /// </summary>
    void OnClosed()
    {
        if (_closed) return;
        _closed = true;
        var total = System.Diagnostics.Stopwatch.StartNew();
        _vm.Dispose();
        long vm = total.ElapsedMilliseconds;
        WindowCleanup.ReleaseTitleBars(this);
        long bars = total.ElapsedMilliseconds - vm;
        var line = $"Fermeture des paramètres : {total.ElapsedMilliseconds} ms (réglages en attente et abonnements {vm} ms, barres de titre {bars} ms)";
        if (total.ElapsedMilliseconds > 100) Log.Warn(line); else Log.Info(line);
    }

    bool _closed;

    /// <inheritdoc/>
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false) =>
        Task.FromResult(PromptWindow.Confirm(this, title, message, confirmText, danger));

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
}
