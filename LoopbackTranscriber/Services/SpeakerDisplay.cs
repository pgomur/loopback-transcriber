using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public static class SpeakerDisplay
{
    public static string Localize(string? speaker)
    {
        if (!SpeakerLabels.IsSelf(speaker))
        {
            return Strings.Instance.Get("SpeakerOthers");
        }

        return SpeakerLabels.SelfName(speaker) is { } name
            ? Strings.Instance.Format("SpeakerSelfNamed", name)
            : Strings.Instance.Get("SpeakerSelf");
    }
}
