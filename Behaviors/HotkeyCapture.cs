using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TouchDeck.ViewModels;

namespace TouchDeck.Behaviors;

/// <summary>Sur un TextBox : une combinaison clavier (Ctrl/Alt/Win + touche, ou touche F) est écrite sous la forme « ctrl+shift+m ».</summary>
public static class HotkeyCapture
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(HotkeyCapture), new PropertyMetadata(false, OnChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        tb.PreviewKeyDown -= OnKeyDown;
        if ((bool)e.NewValue) tb.PreviewKeyDown += OnKeyDown;
    }

    static string? KeyName(Key k) => k switch
    {
        >= Key.A and <= Key.Z => k.ToString().ToLowerInvariant(),
        >= Key.D0 and <= Key.D9 => ((int)k - (int)Key.D0).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)k - (int)Key.NumPad0).ToString(),
        >= Key.F1 and <= Key.F24 => k.ToString().ToLowerInvariant(),
        Key.Enter => "enter", Key.Escape => "esc", Key.Tab => "tab", Key.Space => "space",
        Key.Back => "backspace", Key.Delete => "delete", Key.Insert => "insert",
        Key.Home => "home", Key.End => "end", Key.PageUp => "pageup", Key.Next => "pagedown",
        Key.Left => "left", Key.Up => "up", Key.Right => "right", Key.Down => "down",
        Key.Snapshot => "printscreen",
        _ => null,
    };

    static void OnKeyDown(object sender, KeyEventArgs e)
    {
        var tb = (TextBox)sender;
        // le champ « valeur » sert à tous les types d'action : la capture ne vaut que pour les raccourcis
        if (tb.DataContext is ActionStepViewModel { Type: not "hotkey" }) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        var name = KeyName(key);
        if (name == null) return;
        var mods = Keyboard.Modifiers;
        bool isF = key >= Key.F1 && key <= Key.F24;
        // Sans Ctrl/Alt/Win (et hors touches F), on laisse la saisie manuelle normale
        if (!isF && (mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0) return;

        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("win");
        parts.Add(name);
        tb.Text = string.Join("+", parts);
        tb.CaretIndex = tb.Text.Length;
        e.Handled = true;
    }
}
