using System.Text.RegularExpressions;

namespace LoopbackTranscriber.Tests.Models;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class SpeakerLabelsTests
{
    [Fact]
    public void PersistedLabelValues_AreUnchanged()
    {
        Assert.Equal("Participantes", SpeakerLabels.Others);
        Assert.Equal("Tu", SpeakerLabels.SelfAnonymous);
        Assert.Equal(" (Tú)", SpeakerLabels.SelfNamedSuffix);
    }

    [Theory]
    [InlineData(null, "Tu")]
    [InlineData("  ", "Tu")]
    [InlineData("pgomur", "pgomur (Tú)")]
    [InlineData("pgo: mur", "pgo mur (Tú)")]
    public void ForSelf_CleansTheName(string? name, string expected)
    {
        Assert.Equal(expected, SpeakerLabels.ForSelf(name));
    }

    [Theory]
    [InlineData("Tu", true, "Tú")]
    [InlineData("pgomur (Tú)", true, "pgomur (Tú)")]
    [InlineData("Participantes", false, "Participantes")]
    [InlineData(null, false, "Participantes")]
    public void IsSelfAndDisplayName_FollowTheStoredLabel(string? speaker, bool isSelf, string display)
    {
        Assert.Equal(isSelf, SpeakerLabels.IsSelf(speaker));
        Assert.Equal(display, SpeakerDisplay.Localize(speaker));
    }

    [Theory]
    [InlineData("Tu: hola", true)]
    [InlineData("Participantes: hola", true)]
    [InlineData("pgo mur (Tú): hola", true)]
    [InlineData("Maria: hola", false)]
    public void PrefixPattern_RecognisesOnlyOurLabels(string line, bool expected)
    {
        var regex = new Regex("^(?:" + SpeakerLabels.PrefixPattern + @")\s*:\s*");
        Assert.Equal(expected, regex.IsMatch(line));
    }

    [Fact]
    public void ComposedTranscript_IsParsedBackIntoTheSameSpeakers()
    {
        var other = new List<TranscriptSegment> { new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Buenos dias equipo.") };
        var mic = new List<TranscriptSegment> { new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Presupuesto aprobado.") };

        var text = TranscriptComposer.Compose(other, mic, TimeSpan.FromSeconds(10), "pgomur");
        var messages = TranscriptMessageParser.Parse(text);

        Assert.Equal(2, messages.Count);
        Assert.False(messages[0].IsSelf);
        Assert.Equal(SpeakerLabels.Others, messages[0].Speaker);
        Assert.True(messages[1].IsSelf);
        Assert.Equal(SpeakerLabels.ForSelf("pgomur"), messages[1].Speaker);
    }

    [Fact]
    public void ComposedTranscriptWithoutName_UsesTheAnonymousSelfLabel()
    {
        var other = new List<TranscriptSegment> { new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Hola.") };
        var mic = new List<TranscriptSegment> { new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Buenas.") };

        var messages = TranscriptMessageParser.Parse(TranscriptComposer.Compose(other, mic, TimeSpan.FromSeconds(10), null));

        Assert.True(messages[1].IsSelf);
        Assert.Equal("Tú", messages[1].DisplaySpeaker);
    }

    [Fact]
    public void SummaryPrompts_MentionTheRealLabels_AndLeaveNoPlaceholders()
    {
        foreach (var prompt in new[] { SummaryPrompts.Final("texto", "espanol"), SummaryPrompts.Chunk("texto", 1, 2) })
        {
            Assert.Contains($"\"{SpeakerLabels.SelfAnonymous}\"", prompt);
            Assert.Contains($"\"Nombre{SpeakerLabels.SelfNamedSuffix}\"", prompt);
            Assert.Contains($"\"{SpeakerLabels.Others}\"", prompt);
            Assert.DoesNotContain("{{SELF", prompt);
            Assert.DoesNotContain("{{OTHERS}}", prompt);
        }
    }
}
