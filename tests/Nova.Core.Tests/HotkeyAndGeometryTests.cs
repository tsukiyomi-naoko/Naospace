using Nova.Core.Input;
using Nova.Core.Layout;

namespace Nova.Core.Tests;

public class HotkeyAndGeometryTests
{
    [Theory]
    [InlineData("Ctrl+Alt+N", HotkeyModifiers.Control | HotkeyModifiers.Alt, "N", "Ctrl+Alt+N")]
    [InlineData("win + shift + space", HotkeyModifiers.Windows | HotkeyModifiers.Shift, "Space", "Shift+Win+Space")]
    [InlineData("Ctrl+F12", HotkeyModifiers.Control, "F12", "Ctrl+F12")]
    [InlineData("Alt+1", HotkeyModifiers.Alt, "D1", "Alt+1")]
    public void ParsesValidHotkeys(string text, HotkeyModifiers mods, string key, string roundTrip)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(mods, g!.Modifiers);
        Assert.Equal(key, g.Key);
        Assert.Equal(roundTrip, g.ToString());
    }

    [Theory]
    [InlineData("N")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Hyper+N")]
    [InlineData("Ctrl+F99")]
    [InlineData(null)]
    public void RejectsInvalidHotkeys(string? text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    private static readonly MonitorInfo Primary = new(@"\\.\DISPLAY1", new RectD(0, 0, 1920, 1040), true);
    private static readonly MonitorInfo Ultrawide = new(@"\\.\DISPLAY2", new RectD(1920, 0, 3440, 1400), false);

    [Fact]
    public void KeepsWindowOnSecondaryMonitor()
    {
        var r = new RectD(3000, 100, 600, 800);
        var (bounds, mon) = ScreenGeometry.EnsureVisible(r, Ultrawide.DeviceName, new[] { Primary, Ultrawide });
        Assert.Equal(r, bounds);
        Assert.Equal(Ultrawide.DeviceName, mon.DeviceName);
    }

    [Fact]
    public void MovesWindowWhenMonitorDisconnected()
    {
        var r = new RectD(3000, 100, 600, 1300);
        var (bounds, mon) = ScreenGeometry.EnsureVisible(r, Ultrawide.DeviceName, new[] { Primary });
        Assert.Equal(Primary.DeviceName, mon.DeviceName);
        Assert.True(bounds.Right <= 1920 && bounds.Bottom <= 1040 && bounds.X >= 0 && bounds.Y >= 0);
        Assert.Equal(1040, bounds.Height);
    }

    [Fact]
    public void CentersWhenNoSavedPosition()
    {
        var (bounds, _) = ScreenGeometry.EnsureVisible(new RectD(double.NaN, double.NaN, 1000, 600), null, new[] { Ultrawide, Primary });
        Assert.Equal(460, bounds.X);
        Assert.Equal(220, bounds.Y);
    }

    [Fact]
    public void TitleBarAboveScreenIsPulledBack()
    {
        var (bounds, _) = ScreenGeometry.EnsureVisible(new RectD(100, -200, 600, 600), null, new[] { Primary });
        Assert.Equal(0, bounds.Y);
    }
}
