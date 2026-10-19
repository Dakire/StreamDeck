using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TouchDeck.Controls;
using TouchDeck.ViewModels;

namespace TouchDeck;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow, ISettingsShell
{
    readonly SettingsViewModel _vm;

    public SettingsWindow()
    {
        _vm = new SettingsViewModel(this);
        DataContext = _vm;
        InitializeComponent();
        ShortcutList.ItemsSource = SettingsViewModel.Shortcuts.Select(s => new { s.Keys, s.What }).ToList();
        Icon = Application.Current.MainWindow?.Icon;
        Closed += (_, _) => _vm.Dispose();
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false) =>
        Task.FromResult(PromptWindow.Confirm(this, title, message, confirmText, danger));

    public string? PickOpenFile(string title, string filter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string defaultName)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = filter, FileName = defaultName, OverwritePrompt = true };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }
}
