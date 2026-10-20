using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public sealed class UserNoticePresenter : IDisposable
{
    private readonly UserNotices _notices;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _dispatcher;
    private bool _active;

    public UserNoticePresenter(UserNotices notices, IDialogService dialogs, IUiDispatcher dispatcher)
    {
        _notices = notices;
        _dialogs = dialogs;
        _dispatcher = dispatcher;
        _notices.Added += OnAdded;
    }

    public void Activate()
    {
        _active = true;
        ShowPending();
    }

    private void OnAdded() => _dispatcher.Run(() =>
    {
        if (_active)
        {
            ShowPending();
        }
    });

    private void ShowPending()
    {
        var pending = _notices.TakeAll();
        if (pending.Count == 0)
        {
            return;
        }

        _dialogs.ShowInfo(string.Join("\n\n", pending.Select(m => m.Localize())), Strings.Instance.Get("NoticeTitle"));
    }

    public void Dispose() => _notices.Added -= OnAdded;
}
