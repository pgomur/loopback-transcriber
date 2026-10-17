using System.Collections.ObjectModel;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

internal sealed class LiveTranscriptModel
{
    public const int MaxLines = 300;

    private const int StableDraftMinLength = 20;
    private const double StableDraftMinRatio = 0.6;

    public ObservableCollection<LiveLine> Lines { get; } = new();

    public string DraftText { get; private set; } = string.Empty;

    public string DraftTranslationText { get; private set; } = string.Empty;

    public bool TranslationAvailable { get; set; }

    public string DraftMainText => LiveTextSplitter.ToLines(TranslationAvailable ? DraftTranslationText : DraftText);

    public string DraftOriginalText =>
        TranslationAvailable && DraftTranslationText.Length > 0 ? LiveTextSplitter.ToLines(DraftText) : string.Empty;

    public void Reset()
    {
        Lines.Clear();
        DraftText = string.Empty;
        DraftTranslationText = string.Empty;
        TranslationAvailable = false;
    }

    public void ClearDrafts()
    {
        DraftText = string.Empty;
        DraftTranslationText = string.Empty;
    }

    public LiveLine AddLine(int id, string text, DateTime at, bool showOriginal)
    {
        var state = TranslationAvailable ? LiveTranslationState.Pending : LiveTranslationState.None;
        var line = new LiveLine(id, text, at, state, showOriginal);
        if (TranslationAvailable && DraftTranslationText.Length > 0)
        {
            line.SetProvisionalTranslation(DraftTranslationText);
        }

        Lines.Add(line);
        ClearDrafts();

        while (Lines.Count > MaxLines)
        {
            Lines.RemoveAt(0);
        }

        return line;
    }

    public bool SetDraft(string text)
    {
        if (text.Length > 0 && IsMuchShorter(text, DraftText))
        {
            return false;
        }

        DraftText = text;
        if (text.Length == 0)
        {
            DraftTranslationText = string.Empty;
        }

        return true;
    }

    public bool SetDraftTranslation(string translation)
    {
        if (DraftText.Length == 0 || IsMuchShorter(translation, DraftTranslationText))
        {
            return false;
        }

        DraftTranslationText = translation;
        return true;
    }

    public void SetTranslation(int id, string translation) => FindLine(id)?.SetTranslation(translation);

    public void MarkSkipped(int id) => FindLine(id)?.MarkSkipped();

    public void TranslationFailed()
    {
        TranslationAvailable = false;
        DraftTranslationText = string.Empty;

        foreach (var line in Lines)
        {
            line.MarkSkipped();
        }
    }

    public void SetShowOriginal(bool value)
    {
        foreach (var line in Lines)
        {
            line.SetShowOriginal(value);
        }
    }

    public void UpdateEmphasis(bool draftShown)
    {
        var count = Lines.Count;
        for (var age = 0; age < 6 && age < count; age++)
        {
            var distance = age + (draftShown ? 1 : 0);
            var emphasis = distance switch
            {
                0 => 1.0,
                1 => 0.75,
                2 => 0.6,
                _ => 0.45,
            };
            Lines[count - 1 - age].SetEmphasis(distance == 0, emphasis);
        }
    }

    private static bool IsMuchShorter(string candidate, string current) =>
        current.Length > StableDraftMinLength && candidate.Length < current.Length * StableDraftMinRatio;

    private LiveLine? FindLine(int id)
    {
        for (var i = Lines.Count - 1; i >= 0; i--)
        {
            if (Lines[i].Id == id)
            {
                return Lines[i];
            }
        }

        return null;
    }
}
