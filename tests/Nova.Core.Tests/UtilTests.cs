using Nova.Core.Util;

namespace Nova.Core.Tests;

public class UtilTests
{
    [Theory]
    [InlineData(@"C:\a\b\", @"C:\a\b")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:", @"C:\")]
    [InlineData("\"D:\\x\"", @"D:\x")]
    [InlineData("/home/u/", "/home/u")]
    public void Normalize(string input, string expected) => Assert.Equal(expected, PathUtil.Normalize(input));

    [Fact]
    public void IsUnder_IsCaseInsensitive_AndRespectsSegmentBoundaries()
    {
        Assert.True(PathUtil.IsUnder(@"D:\ComfyUI\models\loras", @"d:\comfyui"));
        Assert.True(PathUtil.IsUnder(@"D:\ComfyUI", @"D:\ComfyUI\"));
        Assert.False(PathUtil.IsUnder(@"D:\ComfyUI2\x", @"D:\ComfyUI"));
    }

    [Fact]
    public void Breadcrumbs_Drive()
    {
        var c = PathUtil.Breadcrumbs(@"C:\Users\nao\Downloads");
        Assert.Equal(new[] { "C:", "Users", "nao", "Downloads" }, c.Select(x => x.Label));
        Assert.Equal(@"C:\", c[0].Path);
        Assert.Equal(@"C:\Users\nao", c[2].Path);
        Assert.Equal(@"C:\Users\nao", PathUtil.Parent(@"C:\Users\nao\Downloads"));
        Assert.Null(PathUtil.Parent(@"C:\"));
    }

    [Fact]
    public void Breadcrumbs_Unc()
    {
        var c = PathUtil.Breadcrumbs(@"\\nas\share\jobs\2026");
        Assert.Equal(@"\\nas\share", c[0].Path);
        Assert.Equal(@"\\nas\share\jobs\2026", c[^1].Path);
    }

    [Fact]
    public void NaturalSort()
    {
        var items = new[] { "img10.png", "img2.png", "Img1.png", "img02.png", "a", "B" };
        var sorted = items.OrderBy(x => x, NaturalStringComparer.Instance).ToArray();
        Assert.Equal(new[] { "a", "B", "Img1.png", "img2.png", "img02.png", "img10.png" }, sorted);
    }

    [Theory]
    [InlineData("Mozaik.exe", "mozaik*", true)]
    [InlineData("Code", "c?de", true)]
    [InlineData("chrome", "*edge*", false)]
    [InlineData("abc", "*", true)]
    public void Wildcards(string text, string pattern, bool expected) =>
        Assert.Equal(expected, WildcardPattern.IsMatch(text, pattern));

    [Fact]
    public void Filter_SubstringGlobAndRegex()
    {
        Assert.True(WildcardPattern.MatchesFilter("ComfyUI - Google Chrome", "comfy"));
        Assert.True(WildcardPattern.MatchesFilter("render_001.png", "*.png"));
        Assert.False(WildcardPattern.MatchesFilter("render_001.png", "*.jpg"));
        Assert.True(WildcardPattern.MatchesFilter("job-2026-17", @"re:job-\d{4}-\d+"));
        Assert.False(WildcardPattern.MatchesFilter("x", "re:(")); // invalid regex never throws
    }

    [Fact]
    public void UniqueNames()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "New folder", "New folder (2)", "a.txt" };
        Assert.Equal("New folder (3)", FileNameHelper.MakeUnique("New folder", existing.Contains, isDirectory: true));
        Assert.Equal("a (2).txt", FileNameHelper.MakeUnique("a.txt", existing.Contains));
        Assert.Equal("b.txt", FileNameHelper.MakeUnique("b.txt", existing.Contains));
    }

    [Theory]
    [InlineData("ok name.txt", true)]
    [InlineData("bad:name", false)]
    [InlineData("CON", false)]
    [InlineData("con.txt", false)]
    [InlineData("trailing.", false)]
    [InlineData("", false)]
    public void ValidateNames(string name, bool valid) => Assert.Equal(valid, FileNameHelper.Validate(name) is null);
}
