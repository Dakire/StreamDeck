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
