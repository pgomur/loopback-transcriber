namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
[Collection(InterfaceLanguageTestGroup.Name)]
public class LocalizationTests
{
    [Fact]
    public void SetLanguage_ChangesTheTexts_AndNotifiesBindingsAndListeners()
    {
        var bindingRefreshes = new List<string?>();
        var languageChanges = 0;
        void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => bindingRefreshes.Add(e.PropertyName);
        void OnLanguageChanged(object? sender, EventArgs e) => languageChanges++;
        Strings.Instance.PropertyChanged += OnPropertyChanged;
        Strings.Instance.LanguageChanged += OnLanguageChanged;

        try
        {
            using (InterfaceLanguage.Use("en"))
            {
                Assert.Equal("en", Strings.Instance.Language);
                Assert.Equal("Delete", Strings.Instance.Get("Delete"));
            }

            Assert.Equal("Eliminar", Strings.Instance.Get("Delete"));
            Assert.Equal(2, languageChanges);
            Assert.Equal(new[] { "Item[]", "Item[]" }, bindingRefreshes);
        }
        finally
        {
            Strings.Instance.PropertyChanged -= OnPropertyChanged;
            Strings.Instance.LanguageChanged -= OnLanguageChanged;
        }
    }

    [Fact]
    public void SetLanguage_ToTheCurrentLanguage_DoesNotNotifyAnyone()
    {
        var languageChanges = 0;
        void OnLanguageChanged(object? sender, EventArgs e) => languageChanges++;
        Strings.Instance.LanguageChanged += OnLanguageChanged;

        try
        {
            Strings.Instance.SetLanguage(InterfaceLanguage.Default);
            Strings.Instance.SetLanguage(InterfaceLanguage.Default.ToUpperInvariant());

            Assert.Equal(0, languageChanges);
        }
        finally
        {
            Strings.Instance.LanguageChanged -= OnLanguageChanged;
        }
    }

    [Fact]
    public void SpeakerLabels_AreShownInTheInterfaceLanguage_WhileTheStoredMarkersStayTheSame()
    {
        using (InterfaceLanguage.Use("en"))
        {
            Assert.Equal("You", SpeakerDisplay.Localize(SpeakerLabels.SelfAnonymous));
            Assert.Equal("pgomur (You)", SpeakerDisplay.Localize(SpeakerLabels.ForSelf("pgomur")));
            Assert.Equal("Participants", SpeakerDisplay.Localize(SpeakerLabels.Others));
            Assert.Equal("pgomur (Tú)", SpeakerLabels.ForSelf("pgomur"));
        }
    }

    [Fact]
    public void UserMessage_IsLocalizedWhenShown_NotWhenCreated()
    {
        var message = UserMessage.From(new InvalidOperationException("boom"));

        Assert.Equal("Error inesperado (boom)", message.Localize());

        using (InterfaceLanguage.Use("de"))
        {
            Assert.Equal("Unerwarteter Fehler (boom)", message.Localize());
        }

        Assert.Equal("Unexpected error (boom)", message.Text);
    }

    [Fact]
    public void DefaultAppName_IsShownInTheInterfaceLanguage()
    {
        using (InterfaceLanguage.Use("en"))
        {
            Assert.Equal(Strings.Instance.GetIn("en", "DefaultAppName"), RecordingNames.Display(RecordingNames.DefaultAppName));
            Assert.Equal(Strings.Instance.GetIn("en", "DefaultAppName"), RecordingNames.Display("reunion"));
        }
    }

    [Fact]
    public void SummarySectionHeadings_FollowTheInterfaceLanguage()
    {
        using (InterfaceLanguage.Use("en"))
        {
            var section = Assert.Single(SummaryFormatter.Parse("Puntos clave:\n- uno"));

            Assert.Equal(Strings.Instance.GetIn("en", "SummaryLabelKeyPoints"), section.Heading);
        }
    }
}
