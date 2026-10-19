using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace TouchDeck;

/// <summary>Petits utilitaires de construction d'interface pour les fenêtres d'édition (thème sombre, cibles tactiles).</summary>
static class Ui
{
    public static string Bg => Theme.EditorBg;
    public static string Surface => Theme.Surface;
    public static string SurfaceHi => Theme.SurfaceHi;
    public static string Line => Theme.Line;
    public static string Accent => Theme.Accent;
    public static string Dim => Theme.Dim;
    public const string Danger = "#B23A48";

    public static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    public static Brush? TryBrush(string hex)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(hex)!; }
        catch { return null; }
    }

    public static Style TextBoxStyle => (Style)XamlReader.Parse((@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'>
  <Setter Property='Foreground' Value='@TEXT'/>
  <Setter Property='CaretBrush' Value='@TEXT'/>
  <Setter Property='SelectionBrush' Value='#5B7CFA'/>
  <Setter Property='Background' Value='@INPUT'/>
  <Setter Property='Padding' Value='12,10'/>
  <Setter Property='FontSize' Value='16'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='TextBox'>
        <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='@LINE' BorderThickness='1' CornerRadius='12'>
          <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsKeyboardFocused' Value='True'>
            <Setter TargetName='Bd' Property='BorderBrush' Value='#5B7CFA'/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>").Replace("@TEXT", Theme.Text).Replace("@INPUT", Theme.Input).Replace("@LINE", Theme.Line));

    static Style Flat => (Style)Application.Current.FindResource("FlatButton");

    public static TextBox Input(string value, Action<string> onChange)
    {
        var tb = new TextBox { Style = TextBoxStyle, Text = value ?? "" };
        tb.TextChanged += (_, _) => onChange(tb.Text);
        return tb;
    }

    public static TextBlock Caption(string text) => new()
    {
        Text = text, Foreground = B(Dim), FontSize = 13, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(2, 12, 0, 6),
    };

    public static TextBlock Hint(string text) => new()
    {
        Text = text, Foreground = B(Dim), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 6, 0, 0),
    };

    public static Button Chip(string text, bool selected, Action onClick, double height = 44, string? bg = null)
    {
        var b = new Button
        {
            Style = Flat, Height = height, MinWidth = height,
            Background = B(bg ?? (selected ? Accent : SurfaceHi)), Tag = new CornerRadius(height / 2),
            Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(16, 0, 16, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new TextBlock { Text = text, FontSize = 15, Foreground = (selected || bg != null) ? Brushes.White : B(Theme.Text) },
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button IconButton(string glyph, Action onClick) => Chip(glyph, false, onClick, 40);

}

/// <summary>Base des fenêtres d'édition : modale, au premier plan, centrée sur la fenêtre principale, thème sombre.</summary>
public abstract class EditorBase : Window
{
    protected EditorBase(Window owner, string title, double width, double height)
    {
        Title = title; Owner = owner; Width = width;
        Height = Math.Min(height, SystemParameters.WorkArea.Height * 0.94);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        Background = Ui.B(Ui.Bg); Foreground = Ui.B(Theme.Text); Icon = owner.Icon;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
    }

    protected Button Footer(string text, string bg, Action onClick)
    {
        var b = Ui.Chip(text, false, onClick, 48, bg);
        b.Margin = new Thickness(8, 0, 0, 0);
        return b;
    }

    protected void Finish(bool ok) { DialogResult = ok; Close(); }
}
