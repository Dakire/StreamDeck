using Xunit;

namespace StreamDak.Tests;

public class TapRecognizerTests
{
    [Fact]
    public void Tap_WithoutMovement_IsATap()
    {
        var t = new TapRecognizer { Slop = 10 };
        t.Down(1, 100, 100);
        Assert.False(t.Move(1, 103, 102));
        Assert.True(t.Up(1, 104, 101));
    }

    [Fact]
    public void Drag_BeyondSlop_IsNotATap_EvenIfFingerReturns()
    {
        var t = new TapRecognizer { Slop = 10 };
        t.Down(1, 100, 100);
        Assert.True(t.Move(1, 140, 100));
        Assert.False(t.Up(1, 100, 100));
    }

    [Fact]
    public void SecondFinger_IsIgnoredWhileOneIsTracked()
    {
        var t = new TapRecognizer { Slop = 10 };
        t.Down(1, 100, 100);
        t.Down(2, 500, 500);
        Assert.False(t.Up(2, 500, 500));
        Assert.True(t.Up(1, 100, 100));
    }

    [Fact]
    public void AfterUp_ANewTapCanStart()
    {
        var t = new TapRecognizer { Slop = 10 };
        t.Down(1, 0, 0); Assert.True(t.Up(1, 0, 0));
        t.Down(2, 50, 50); Assert.True(t.Up(2, 50, 50));
    }
}
