using CommunityToolkit.Mvvm.Messaging;

namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Unit)]
public sealed class TargetsViewModelTests : IDisposable
{
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FakeTargetWatcher _watcher = new();
    private readonly FakeCustomApps _customApps = new();
    private readonly TargetsViewModel _targets;

    public TargetsViewModelTests()
    {
        _targets = new TargetsViewModel(_watcher, _customApps, _messenger);
    }

    public void Dispose() => _targets.Dispose();

    [Fact]
    public void Items_ContainTheBuiltInApps_AndAreRegisteredInTheWatcher()
    {
        Assert.Contains(_targets.Items, t => t.App.Id == "teams");
        Assert.Contains("teams", _watcher.Added);
    }

    [Fact]
    public void SelectionChangedMessage_IsSentOnlyWhenTheSelectionActuallyChanges()
    {
        var raised = new List<TargetItemViewModel?>();
        _messenger.Register<List<TargetItemViewModel?>, TargetSelectionChangedMessage>(raised, static (list, m) => list.Add(m.Target));
        var first = _targets.Items[0];

        _targets.SelectedTarget = first;
        _targets.SelectedTarget = first;
        _targets.SelectedTarget = null;

        Assert.Equal(2, raised.Count);
        Assert.Same(first, raised[0]);
        Assert.Null(raised[1]);
    }

    [Fact]
    public void Select_ChangesTheSelection_AndAnnouncesItOnce()
    {
        var raised = new List<TargetItemViewModel?>();
        _messenger.Register<List<TargetItemViewModel?>, TargetSelectionChangedMessage>(raised, static (list, m) => list.Add(m.Target));
        var app = _customApps.AddApp("zoom", "Zoom");
        var target = _targets.Items.First(t => t.App.Id == app.Id);
        raised.Clear();

        _targets.Select(target);

        Assert.Same(target, _targets.SelectedTarget);
        Assert.Same(target, Assert.Single(raised));
    }

    [Fact]
    public void Select_AnnouncesTheSelectionAgain_WhenTheTargetWasAlreadySelected()
    {
        var target = _targets.Items[0];
        _targets.SelectedTarget = target;
        var raised = 0;
        _messenger.Register<object, TargetSelectionChangedMessage>(this, (_, _) => raised++);

        _targets.Select(target);

        Assert.Equal(1, raised);
        Assert.Same(target, _targets.SelectedTarget);
    }

    [Fact]
    public void Select_IgnoresATargetThatIsNotInTheList()
    {
        var raised = 0;
        _messenger.Register<object, TargetSelectionChangedMessage>(this, (_, _) => raised++);

        _targets.Select(new TargetItemViewModel(LoopbackTranscriber.Models.TargetApp.Teams));

        Assert.Null(_targets.SelectedTarget);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void SelectFirstIfNone_SelectsTheFirstItemOnce()
    {
        var raised = 0;
        _messenger.Register<object, TargetSelectionChangedMessage>(this, (_, _) => raised++);

        _targets.SelectFirstIfNone();
        _targets.SelectFirstIfNone();

        Assert.Same(_targets.Items[0], _targets.SelectedTarget);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void RefreshActiveStates_MarksOnlyTheSelectedTargetAndOnlyInTheAppView()
    {
        _targets.SelectedTarget = _targets.Items[0];

        _targets.RefreshActiveStates(appViewActive: true);
        Assert.True(_targets.Items[0].IsActive);
        Assert.All(_targets.Items.Skip(1), t => Assert.False(t.IsActive));

        _targets.RefreshActiveStates(appViewActive: false);
        Assert.All(_targets.Items, t => Assert.False(t.IsActive));
    }

    [Fact]
    public void Start_StartsTheWatcher_AndAnnouncesTheSyncedStatuses()
    {
        var synced = 0;
        _messenger.Register<object, TargetStatusesSyncedMessage>(this, (_, _) => synced++);

        _targets.Start();

        Assert.True(_watcher.Started);
        Assert.Equal(1, synced);
    }

    [Fact]
    public void WatcherChanges_UpdateTheItems_AndAnnounceTheSync()
    {
        var synced = 0;
        _messenger.Register<object, TargetStatusesSyncedMessage>(this, (_, _) => synced++);
        var teams = _targets.Items.First(t => t.App.Id == "teams");

        _watcher.SetRunning("teams", running: true, processId: 99);

        Assert.True(teams.IsRunning);
        Assert.Equal(99u, teams.ProcessId);
        Assert.Equal(1, synced);
    }

    [Fact]
    public void AddingAndRemovingACustomApp_UpdatesTheListAndTheWatcher()
    {
        var changes = 0;
        _messenger.Register<object, TargetListChangedMessage>(this, (_, _) => changes++);

        var app = _customApps.AddApp("notepad", "Bloc");
        Assert.Contains(_targets.Items, t => t.App.Id == app.Id);
        Assert.Contains(app.Id, _watcher.Added);

        _customApps.RemoveApp(app.Id);
        Assert.DoesNotContain(_targets.Items, t => t.App.Id == app.Id);
        Assert.Contains(app.Id, _watcher.Removed);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void RemovingTheSelectedCustomApp_ClearsTheSelection()
    {
        var app = _customApps.AddApp("notepad", "Bloc");
        _targets.SelectedTarget = _targets.Items.First(t => t.App.Id == app.Id);

        _customApps.RemoveApp(app.Id);

        Assert.Null(_targets.SelectedTarget);
    }
}
