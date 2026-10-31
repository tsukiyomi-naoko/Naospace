using Nova.Core.Model;

namespace Nova.Core.Tests;

public class PinTreeTests
{
    private static (List<PinNode> roots, PinNode models, PinNode lora, PinNode output) Build()
    {
        var lora = PinNode.Folder(@"D:\ComfyUI\models\loras", "LoRAs");
        var models = PinNode.Group("Models", PinNode.Folder(@"D:\ComfyUI\models\checkpoints"), lora);
        var output = PinNode.Folder(@"D:\ComfyUI\output");
        return (new List<PinNode> { PinNode.Folder(@"D:\ComfyUI"), models, output }, models, lora, output);
    }

    [Fact]
    public void MoveIntoGroup()
    {
        var (roots, models, _, output) = Build();
        Assert.True(PinTree.Move(roots, output.Id, models.Id, intoGroup: true));
        Assert.Contains(output, models.Children);
        Assert.DoesNotContain(output, roots);
    }

    [Fact]
    public void ReorderBeforeTarget()
    {
        var (roots, models, _, output) = Build();
        Assert.True(PinTree.Move(roots, output.Id, models.Id, intoGroup: false));
        Assert.Equal(1, roots.IndexOf(output));
    }

    [Fact]
    public void GroupCannotMoveIntoItsOwnDescendant()
    {
        var (roots, models, lora, _) = Build();
        var nested = PinNode.Group("Nested");
        models.Children.Add(nested);
        Assert.False(PinTree.Move(roots, models.Id, nested.Id, intoGroup: true));
        Assert.Contains(models, roots);
        Assert.Contains(lora, models.Children);
    }

    [Fact]
    public void DeriveLabels()
    {
        Assert.Equal("loras", PinNode.DeriveLabel(@"D:\ComfyUI\models\loras\", PinKind.Folder));
        Assert.Equal(@"D:\", PinNode.DeriveLabel(@"D:\", PinKind.Folder));
        Assert.Equal("Code.exe", PinNode.DeriveLabel(@"C:\Program Files\Code\Code.exe", PinKind.Application));
        Assert.Equal("https://x.y", PinNode.DeriveLabel("https://x.y", PinKind.Url));
    }

    [Fact]
    public void DeepCloneWithNewIdsIsIndependent()
    {
        var (_, models, _, _) = Build();
        var clone = models.DeepClone(newIds: true);
        Assert.NotEqual(models.Id, clone.Id);
        Assert.NotEqual(models.Children[0].Id, clone.Children[0].Id);
        clone.Children.Clear();
        Assert.Equal(2, models.Children.Count);
    }
}
