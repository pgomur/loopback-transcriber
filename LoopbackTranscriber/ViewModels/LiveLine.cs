using CommunityToolkit.Mvvm.ComponentModel;

namespace LoopbackTranscriber.ViewModels;

public enum LiveTranslationState
{
    None,
    Pending,
    Done,
    Skipped,
}

public sealed class LiveLine : ObservableObject
{
    private string _translation = string.Empty;
    private LiveTranslationState _state;
    private bool _showOriginal;

    public LiveLine(int id, string original, DateTime at, LiveTranslationState state, bool showOriginal = true)
    {
        Id = id;
        Original = original;
        At = at;
        _state = state;
        _showOriginal = showOriginal;
    }

    public int Id { get; }

    public string Original { get; }

    public DateTime At { get; }

    private string _provisional = string.Empty;

    public string TranslationDisplay => _state == LiveTranslationState.Done ? _translation : _provisional;

    public bool ShowTranslation =>
        _state == LiveTranslationState.Done || (_state == LiveTranslationState.Pending && _provisional.Length > 0);

    public void SetProvisionalTranslation(string translation)
    {
        if (_state != LiveTranslationState.Pending || translation.Length == 0)
        {
            return;
        }

        _provisional = translation;
        NotifyState();
    }

    public bool ShowOriginalMain => !ShowTranslation;

    public bool ShowOriginalSmall => ShowTranslation && _showOriginal;

    private bool _isCurrent;
    private double _emphasis = 1.0;

    public bool IsCurrent => _isCurrent;

    public double Emphasis => _emphasis;

    public void SetEmphasis(bool isCurrent, double emphasis)
    {
        if (_isCurrent == isCurrent && Math.Abs(_emphasis - emphasis) < 0.001)
        {
            return;
        }

        _isCurrent = isCurrent;
        _emphasis = emphasis;
        OnPropertyChanged(nameof(IsCurrent));
        OnPropertyChanged(nameof(Emphasis));
    }

    public void SetShowOriginal(bool value)
    {
        if (_showOriginal == value)
        {
            return;
        }

        _showOriginal = value;
        OnPropertyChanged(nameof(ShowOriginalSmall));
    }

    public double MainOpacity => _state switch
    {
        LiveTranslationState.Pending => _provisional.Length > 0 ? 0.85 : 0.6,
        _ => 1.0,
    };

    public void SetTranslation(string translation)
    {
        _translation = translation;
        _state = LiveTranslationState.Done;
        NotifyState();
    }

    public void MarkSkipped()
    {
        if (_state != LiveTranslationState.Pending)
        {
            return;
        }

        _state = LiveTranslationState.Skipped;
        NotifyState();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(TranslationDisplay));
        OnPropertyChanged(nameof(ShowTranslation));
        OnPropertyChanged(nameof(ShowOriginalMain));
        OnPropertyChanged(nameof(ShowOriginalSmall));
        OnPropertyChanged(nameof(MainOpacity));
    }
}
