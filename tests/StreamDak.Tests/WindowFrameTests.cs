using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Xunit;

namespace StreamDak.Tests;

/// <summary>
/// Barre de titre compatible Windows 11 : le bouton plein écran est annoncé comme bouton agrandir (disposition d'accrochage au survol), la fenêtre est
/// redimensionnable et accrochable, et le deck reste une fenêtre qui ne prend jamais le focus. Vraie fenêtre Win32, placée hors écran.
/// </summary>
[Collection("ui")]
public class WindowFrameTests
{
    const int WM_NCHITTEST = 0x0084, HTCLIENT = 1, HTMAXBUTTON = 9;
    const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    const int WS_THICKFRAME = 0x00040000, WS_MAXIMIZEBOX = 0x00010000, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    readonly WpfHost _host;
    public WindowFrameTests(WpfHost host) => _host = host;

    static IntPtr Lparam(int x, int y) => new((y << 16) | (x & 0xFFFF));

    /// <summary>Une fenêtre du deck réellement affichée, loin hors de tout écran (jamais visible, jamais activée).</summary>
    MainWindow OffscreenDeck()
    {
        _host.Reset(new DeckConfig
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
            Profiles = { new DeckProfile { Name = "P", Pages = { new DeckPage { Name = "A" } } } },
        });
        var win = _host.Run(() => new MainWindow(_host.Services) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 });
        _host.Run(win.Show);
        _host.Settle();
        return win;
    }

    static IntPtr Hwnd(Window w) => new WindowInteropHelper(w).Handle;

    /// <summary>Centre d'un élément en pixels physiques de l'écran.</summary>
    static (int X, int Y) CenterOf(FrameworkElement e)
    {
        var p = e.PointToScreen(new Point(e.ActualWidth / 2, e.ActualHeight / 2));
        return ((int)p.X, (int)p.Y);
    }

    [Fact]
    public void TheWindowCanBeSnappedAndResizedButStillNeverTakesTheFocus()
    {
        var win = OffscreenDeck();
        try
        {
            _host.Run(() =>
            {
                int style = GetWindowLong(Hwnd(win), GWL_STYLE);
                Assert.NotEqual(0, style & WS_THICKFRAME);      // sans cadre redimensionnable, Windows n'accroche pas la fenêtre
                Assert.NotEqual(0, style & WS_MAXIMIZEBOX);     // ni ne propose la disposition d'accrochage
                Assert.NotEqual(0, GetWindowLong(Hwnd(win), GWL_EXSTYLE) & WS_EX_NOACTIVATE);   // cœur du produit : jamais le focus
            });
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void WindowsSeesTheFullscreenButtonAsTheMaximizeButtonOnlyForTheMouse()
    {
        var win = OffscreenDeck();
        try
        {
            _host.Run(() =>
            {
                var max = (FrameworkElement)win.FindName("MaxButton");
                var (x, y) = CenterOf(max);
                Assert.Equal(HTMAXBUTTON, (int)SendMessage(Hwnd(win), WM_NCHITTEST, IntPtr.Zero, Lparam(x, y)));   // la disposition d'accrochage s'affiche
                var press = (UIElement)((System.Windows.Controls.Button)max).Template.FindName("Press", max);
                Assert.Equal(0.08, press.Opacity);                                                                     // WPF ne voit plus le survol : la couche est allumée à la main

                var beside = CenterOf((FrameworkElement)win.FindName("DragBar"));
                Assert.NotEqual(HTMAXBUTTON, (int)SendMessage(Hwnd(win), WM_NCHITTEST, IntPtr.Zero, Lparam(beside.X, beside.Y)));   // le reste de la barre n'est pas le bouton
                Assert.Equal(0, press.Opacity);                                                                        // et le survol s'éteint en sortant

                // Après un appui du doigt, plus de bouton agrandir (le doigt n'a pas de survol) : la barre fonctionne comme avant
                typeof(MainWindow).GetField("_lastTouchTick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(win, Environment.TickCount64);
                Assert.NotEqual(HTMAXBUTTON, (int)SendMessage(Hwnd(win), WM_NCHITTEST, IntPtr.Zero, Lparam(x, y)));
            });
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void NoMaximizeButtonUnderAMenuOrADialog()
    {
        var win = OffscreenDeck();
        try
        {
            _host.Run(() =>
            {
                var vm = (ViewModels.MainViewModel)win.DataContext;
                var (x, y) = CenterOf((FrameworkElement)win.FindName("MaxButton"));
                vm.OpenMenuCommand.Execute(null);
                Assert.NotEqual(HTMAXBUTTON, (int)SendMessage(Hwnd(win), WM_NCHITTEST, IntPtr.Zero, Lparam(x, y)));   // le menu ouvert recouvre la barre
                vm.CloseMenuCommand.Execute(null);
                Assert.Equal(HTMAXBUTTON, (int)SendMessage(Hwnd(win), WM_NCHITTEST, IntPtr.Zero, Lparam(x, y)));
            });
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void InFullscreenTheEdgesDoNotResizeSoTheTouchGoesToTheTiles()
    {
        var win = OffscreenDeck();
        try
        {
            _host.Run(() =>
            {
                var hwnd = Hwnd(win);
                GetWindowRect(hwnd, out var r);
                int midY = (r.Top + r.Bottom) / 2;
                // Fenêtre normale : le bord gauche est une poignée de redimensionnement (ou au moins n'est pas forcé en zone cliente)
                int normal = (int)SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, Lparam(r.Left + 1, midY));

                typeof(MainWindow).GetField("_full", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(win, true);
                Assert.Equal(HTCLIENT, (int)SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, Lparam(r.Left + 1, midY)));
                Assert.Equal(HTCLIENT, (int)SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, Lparam(r.Right - 2, midY)));
                Assert.Equal(HTCLIENT, (int)SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, Lparam((r.Left + r.Right) / 2, r.Bottom - 2)));
                Assert.NotEqual(HTCLIENT, normal);                      // et c'était bien une poignée avant (le test ne prouve pas rien)
            });
        }
        finally { _host.Run(win.Close); }
    }
}
