using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public interface ILiveTranslationFactory
{
    LiveTranslationViewModel Create(TargetItemViewModel target, Action openMainWindow);
}

public sealed class LiveTranslationFactory(IRecordingHost host, ISettingsStore settings) : ILiveTranslationFactory
{
    public LiveTranslationViewModel Create(TargetItemViewModel target, Action openMainWindow) =>
        new(host, target, settings, openMainWindow);
}
