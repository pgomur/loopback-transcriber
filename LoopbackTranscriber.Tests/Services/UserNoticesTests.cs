namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class UserNoticesTests
{
    [Fact]
    public void Add_QueuesTheMessage_AndRaisesAdded()
    {
        var notices = new UserNotices();
        var raised = 0;
        notices.Added += () => raised++;

        notices.Add(UserMessage.FromKey("NoticeSettingsReset"));

        Assert.Equal(1, raised);
        Assert.Equal("NoticeSettingsReset", Assert.Single(notices.TakeAll()).Key);
    }

    [Fact]
    public void AddOnce_ReportsEachIdentityOnlyOnce_IgnoringCase()
    {
        var notices = new UserNotices();

        notices.AddOnce(@"C:\Datos\carpeta", UserMessage.FromKey("NoticeUnreadableTranscript", "a"));
        notices.AddOnce(@"c:\datos\CARPETA", UserMessage.FromKey("NoticeUnreadableTranscript", "a"));
        notices.AddOnce(@"C:\Datos\otra", UserMessage.FromKey("NoticeUnreadableTranscript", "b"));

        Assert.Equal(2, notices.TakeAll().Count);
    }

    [Fact]
    public void AddOnce_StaysSilentAfterTheNoticeWasTaken()
    {
        var notices = new UserNotices();

        notices.AddOnce("settings", UserMessage.FromKey("NoticeSettingsNotSaved"));
        Assert.Single(notices.TakeAll());

        notices.AddOnce("settings", UserMessage.FromKey("NoticeSettingsNotSaved"));
        Assert.Empty(notices.TakeAll());
    }

    [Fact]
    public void TakeAll_ReturnsTheMessagesInOrder_AndEmptiesTheQueue()
    {
        var notices = new UserNotices();
        notices.Add("primero");
        notices.Add("segundo");

        Assert.Equal(new[] { "primero", "segundo" }, notices.TakeAll().Select(m => m.Text));
        Assert.Empty(notices.TakeAll());
    }
}
