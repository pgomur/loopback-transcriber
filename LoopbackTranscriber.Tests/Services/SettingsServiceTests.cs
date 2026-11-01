namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class SettingsServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly UserNotices _notices = new();
    private readonly string _path;

    public SettingsServiceTests()
    {
        _path = _temp.Combine("settings", "settings.json");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void WithoutAFile_DefaultsAreUsed_AndNothingIsWrittenUntilSomethingChanges()
    {
        var service = Create();

        Assert.False(service.Snapshot().FirstRunSetupCompleted);
        Assert.False(File.Exists(_path));
        Assert.Empty(_notices.TakeAll());
    }

    [Fact]
    public void Update_IsSavedToDisk_AndReadBackByTheNextStart()
    {
        Create().Update(s =>
        {
            s.UserName = "pgomur";
            s.LiveWindowOpacity = 0.6;
        });

        var reloaded = Create().Snapshot();

        Assert.Equal("pgomur", reloaded.UserName);
        Assert.Equal(0.6, reloaded.LiveWindowOpacity);
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void Snapshot_IsAnIndependentCopy()
    {
        var service = Create();

        var snapshot = service.Snapshot();
        snapshot.UserName = "cambiado sin Update";
        snapshot.CustomApps.Add(new CustomTargetAppSettings());

        Assert.Equal(string.Empty, service.Snapshot().UserName);
        Assert.Empty(service.Snapshot().CustomApps);
        Assert.NotSame(service.Snapshot(), service.Snapshot());
    }

    [Fact]
    public void AnUpdateThatChangesNothing_DoesNotWrite()
    {
        var service = Create();
        service.Update(s => s.UserName = "pgomur");
        var writtenAt = File.GetLastWriteTimeUtc(_path);
        File.SetLastWriteTimeUtc(_path, writtenAt.AddMinutes(-5));

        service.Update(s => s.UserName = "pgomur");

        Assert.Equal(writtenAt.AddMinutes(-5), File.GetLastWriteTimeUtc(_path));
    }

    [Fact]
    public void ACorruptFile_IsSetAside_AndTheUserIsTold()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ \"UserName\": ");

        var service = Create();

        Assert.Equal(string.Empty, service.Snapshot().UserName);
        Assert.False(File.Exists(_path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_path)!, "settings.json.corrupt-*"));
        Assert.Equal("NoticeSettingsReset", Assert.Single(_notices.TakeAll()).Key);
    }

    [Fact]
    public void WhenTheFileIsLocked_TheChangeIsKeptInMemory_AndSavedWithTheNextChange()
    {
        var service = Create();
        service.Update(s => s.UserName = "pgomur");

        using (Lock())
        {
            service.Update(s => s.TrayHintShown = true);
            service.Update(s => s.TranslucentUi = true);

            Assert.True(service.Snapshot().TrayHintShown);
        }

        Assert.Equal("NoticeSettingsNotSaved", Assert.Single(_notices.TakeAll()).Key);
        Assert.False(new SettingsService(settingsPath: _path).Snapshot().TrayHintShown);

        service.Update(s => s.UserName = "pgomur");

        var reloaded = Create().Snapshot();
        Assert.True(reloaded.TrayHintShown);
        Assert.True(reloaded.TranslucentUi);
    }

    [Fact]
    public void FlushPendingWrite_SavesAChangeThatCouldNotBeSavedBefore()
    {
        var service = Create();
        service.Update(s => s.UserName = "pgomur");

        using (Lock())
        {
            service.Update(s => s.UserName = "Ana");
        }

        service.FlushPendingWrite();

        Assert.Equal("Ana", Create().Snapshot().UserName);
    }

    [Fact]
    public void FlushPendingWrite_WithNothingPending_DoesNotWrite()
    {
        var service = Create();

        service.FlushPendingWrite();

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void ConcurrentUpdates_AreAllKept()
    {
        var service = Create();

        Parallel.For(0, 40, i => service.Update(s => s.CustomApps.Add(new CustomTargetAppSettings { ProcessName = "app" + i })));

        Assert.Equal(40, Create().Snapshot().CustomApps.Count);
    }

    [Fact]
    public void EnsureDefaultUserName_KeepsANameTheUserAlreadyChose()
    {
        Create().Update(s => s.UserName = "pgomur");
        var service = Create();

        service.EnsureDefaultUserName();

        Assert.Equal("pgomur", service.Snapshot().UserName);
        Assert.True(service.Snapshot().UserNameInitialized);
    }

    [Fact]
    public void EnsureDefaultUserName_RunsOnlyOnce_SoAClearedNameStaysCleared()
    {
        Create().Update(s =>
        {
            s.UserName = string.Empty;
            s.UserNameInitialized = true;
        });
        var service = Create();

        service.EnsureDefaultUserName();

        Assert.Equal(string.Empty, service.Snapshot().UserName);
    }

    private SettingsService Create() => new(_notices, _path);

    private FileStream Lock() => new(_path, FileMode.Open, FileAccess.Read, FileShare.None);
}
