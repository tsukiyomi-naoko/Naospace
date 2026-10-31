using Nova.Core.Model;

namespace Nova.Core.Tests;

public class NavigationHistoryTests
{
    [Fact]
    public void BackForwardAndTruncation()
    {
        var h = new NavigationHistory();
        h.Navigate(@"C:\a");
        h.Navigate(@"C:\b");
        h.Navigate(@"C:\c");
        Assert.Equal(@"C:\b", h.GoBack());
        Assert.Equal(@"C:\a", h.GoBack());
        Assert.Null(h.GoBack());
        Assert.Equal(@"C:\b", h.GoForward());
        h.Navigate(@"C:\d");
        Assert.False(h.CanGoForward);
        Assert.Equal(new[] { @"C:\a", @"C:\b", @"C:\d" }, h.Entries);
    }

    [Fact]
    public void DuplicateOfCurrentIsIgnoredCaseInsensitively()
    {
        var h = new NavigationHistory();
        h.Navigate(@"C:\Data");
        h.Navigate(@"c:\data\");
        Assert.Single(h.Entries);
    }

    [Fact]
    public void CapacityIsEnforced()
    {
        var h = new NavigationHistory();
        for (var i = 0; i < 10; i++) h.Navigate($@"C:\{i}", capacity: 5);
        Assert.Equal(5, h.Entries.Count);
        Assert.Equal(@"C:\9", h.Current);
        Assert.Equal(4, h.Index);
    }

    [Fact]
    public void NormalizeClampsCorruptIndex()
    {
        var h = new NavigationHistory { Entries = { "x", "", "y" }, Index = 42 };
        h.Normalize();
        Assert.Equal(new[] { "x", "y" }, h.Entries);
        Assert.Equal("y", h.Current);
    }
}
