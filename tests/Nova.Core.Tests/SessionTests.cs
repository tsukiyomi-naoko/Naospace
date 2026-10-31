using Nova.Core.Model;

namespace Nova.Core.Tests;

public class SessionTests
{
    [Fact]
    public void SnapshotRestoreReplacesLayoutButKeepsIdentity()
    {
        var state = new WorkspaceState();
        var session = state.ActiveSession;
        session.DualPane = true;
        session.Panes[0].Tabs.Add(TabState.At(@"D:\ComfyUI\workflows"));
        session.Panes[1].Tabs.Add(TabState.At(@"D:\ComfyUI\output"));

        var snaps = new SnapshotCollection();
        var snap = snaps.Capture(session, "Animation");

        session.DualPane = false;
        session.Panes[0].Tabs.Clear();
        session.Panes[0].Tabs.Add(TabState.At(@"C:\Elsewhere"));
        var id = session.Id;

        SnapshotCollection.RestoreInto(snap, session);
        Assert.Equal(id, session.Id);
        Assert.True(session.DualPane);
        Assert.Equal(@"D:\ComfyUI\workflows", session.Panes[0].ActiveTab!.CurrentPath);
        // Restoring must not alias the snapshot.
        session.Panes[0].Tabs.Clear();
        Assert.Single(snap.Session.Panes[0].Tabs);
    }

    [Fact]
    public void LastSessionCannotBeRemoved()
    {
        var state = new WorkspaceState();
        var first = state.ActiveSession;
        Assert.False(state.RemoveSession(first.Id));
        var second = state.AddSession("Model management", first);
        state.ActiveSessionId = second.Id;
        Assert.True(state.RemoveSession(second.Id));
        Assert.Equal(first.Id, state.ActiveSessionId);
    }

    [Fact]
    public void WorkingLocationsAreDeduplicated()
    {
        var state = new WorkspaceState();
        Assert.True(state.AddWorkingLocation(@"C:\Temp\Job1"));
        Assert.False(state.AddWorkingLocation(@"c:\temp\job1\"));
        Assert.Single(state.WorkingLocations);
    }

    [Fact]
    public void RecentListOrdersAndCaps()
    {
        var r = new RecentList { Capacity = 3 };
        var t0 = DateTimeOffset.UtcNow;
        r.Touch("a", t0);
        r.Touch("b", t0);
        r.Touch("c", t0);
        r.Touch("A", t0); // case-insensitive re-touch moves to front
        r.Touch("d", t0);
        Assert.Equal(new[] { "d", "a", "c" }, r.Items.Select(i => i.Path));
        Assert.Equal(2, r.Items[1].Count);
        Assert.Equal("a", r.MostFrequent(1, t0).First().Path);
    }
}
