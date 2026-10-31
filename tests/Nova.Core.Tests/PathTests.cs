using Nova.Core.IO;

namespace Nova.Core.Tests;

public class PathTests
{
    [Fact]
    public void SplitsDrivePaths()
    {
        var s = PathSegments.Split(@"d:\ComfyUI\models\loras\");
        Assert.Equal(new[] { @"D:\", "ComfyUI", "models", "loras" }, s.Select(x => x.Label));
        Assert.Equal(@"D:\ComfyUI\models", s[2].FullPath);
    }

    [Fact]
    public void SplitsUncPaths()
    {
        var s = PathSegments.Split(@"\\nas\projects\mozaik\jobs");
        Assert.Equal(new[] { @"\\nas\projects", "mozaik", "jobs" }, s.Select(x => x.Label));
        Assert.Equal(@"\\nas\projects\mozaik", s[1].FullPath);
    }

    [Fact]
    public void VirtualLocationsAreSingleSegment()
    {
        var s = PathSegments.Split(PathSegments.ThisPc, "This PC");
        Assert.Single(s);
        Assert.Equal("This PC", s[0].Label);
    }

    [Fact]
    public void Parents()
    {
        Assert.Equal(@"C:\a", PathSegments.Parent(@"C:\a\b"));
        Assert.Equal(@"C:\", PathSegments.Parent(@"C:\a"));
        Assert.Equal(PathSegments.ThisPc, PathSegments.Parent(@"C:\"));
        Assert.Null(PathSegments.Parent(PathSegments.ThisPc));
    }

    [Fact]
    public async Task ProbeReportsMissingAndAvailable()
    {
        Assert.Equal(PathStatus.Available, await PathProbe.ProbeAsync(Path.GetTempPath(), expectDirectory: true));
        Assert.Equal(PathStatus.Missing, await PathProbe.ProbeAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), true));
        Assert.Equal(PathStatus.Virtual, await PathProbe.ProbeAsync("shell:Downloads", true));
    }
}
