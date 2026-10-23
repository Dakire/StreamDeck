using System;
using Xunit;

namespace StreamDak.Tests;

/// <summary>
/// Le toucher ne doit pas déplacer le curseur : WPF traite lui-même WM_POINTER (promotion en souris WPF, sans DefWindowProc).
/// Ces tests protègent les deux conditions : le commutateur est actif et seul le toucher/stylet est retiré de DefWindowProc.
/// La validation du curseur immobile se fait sur un vrai écran tactile.
/// </summary>
public class TouchPointerFilterTests
{
    const int WM_MOUSEACTIVATE = 0x0021;
    const int MA_NOACTIVATE = 3;

    static bool Filters(int msg, uint? type) => MainWindow.IsTouchOrPenPointerMessage(msg, new IntPtr(7), _ => type);

    [Theory]
    [InlineData(0x0245)]   // WM_POINTERUPDATE
    [InlineData(0x0246)]   // WM_POINTERDOWN
    [InlineData(0x0247)]   // WM_POINTERUP
    public void TouchAndPen_AreSwallowed_MouseAndUnknown_Never(int msg)
    {
        Assert.True(Filters(msg, 2));
        Assert.True(Filters(msg, 3));
        Assert.False(Filters(msg, 1));
        Assert.False(Filters(msg, null));
    }

    [Fact]
    public void OtherMessages_AreNeverSwallowed()
    {
        Assert.False(Filters(0x0200, 2));   // WM_MOUSEMOVE
        Assert.False(Filters(0x0021, 2));   // WM_MOUSEACTIVATE
    }

    [Fact]
    public void MouseActivate_StaysNoActivate()
    {
        bool handled = false;
        var result = MainWindow.WndProc(IntPtr.Zero, WM_MOUSEACTIVATE, IntPtr.Zero, IntPtr.Zero, ref handled);
        Assert.True(handled);
        Assert.Equal(MA_NOACTIVATE, result.ToInt32());
    }
}
