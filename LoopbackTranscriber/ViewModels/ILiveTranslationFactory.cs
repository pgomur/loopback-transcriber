using CommunityToolkit.Mvvm.Messaging;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public interface ILiveTranslationFactory
{
    LiveTranslationViewModel Create(TargetItemViewModel target, Action openMainWindow);
}

public sealed class LiveTranslationFactory(IRecordingHost host, ISettingsStore settings, IMessenger? messenger = null,
    IUiDispatcher? dispatcher = null) : ILiveTranslationFactory
{
    public LiveTranslationViewModel Create(TargetItemViewModel target, Action openMainWindow) =>
        new(host, target, settings, openMainWindow, messenger, dispatcher);
}
