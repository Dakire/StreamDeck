using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TouchDeck.Controls;
using TouchDeck.ViewModels;

namespace TouchDeck;

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
        Closed += (_, _) => _vm.Dispose();
    }

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
