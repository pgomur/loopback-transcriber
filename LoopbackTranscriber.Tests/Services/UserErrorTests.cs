namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class UserErrorTests
{
    [Fact]
    public void FromKey_KeepsTheKeyAndArguments_AndAnEnglishTextForTheLog()
    {
        var message = UserMessage.FromKey("ErrorUnexpected", "boom");

        Assert.Equal("ErrorUnexpected", message.Key);
        Assert.Equal(new object?[] { "boom" }, message.Args);
        Assert.Equal("Unexpected error (boom)", message.Text);
        Assert.Equal("Error inesperado (boom)", message.Localize());
    }

    [Fact]
    public void TaggedException_BecomesAMessageWithItsKey()
    {
        var exception = UserError.InvalidOperation("ErrorNoTextInAudio", 30);

        var message = UserMessage.From(exception);

        Assert.Equal("ErrorNoTextInAudio", message.Key);
        Assert.Equal(new object?[] { 30 }, message.Args);
        Assert.Equal(Strings.Instance.FormatIn("en", "ErrorNoTextInAudio", 30), exception.Message);
        Assert.Equal(Strings.Instance.Format("ErrorNoTextInAudio", 30), message.Localize());
        Assert.Equal(Strings.Instance.Format("ErrorNoTextInAudio", 30), exception.ToUserMessage());
    }

    [Fact]
    public void UntaggedException_IsShownAsAnUnexpectedErrorInTheUsersLanguage_WithTheTechnicalDetail()
    {
        var exception = new InvalidOperationException("Invalid summary job.");

        Assert.Equal("Error inesperado (Invalid summary job.)", UserMessage.From(exception).Localize());
        Assert.Equal("Error inesperado (Invalid summary job.)", exception.ToUserMessage());
        Assert.Equal(UserError.UnexpectedKey, UserMessage.From(exception).Key);
    }

    [Fact]
    public void Factories_CreateTheExpectedExceptionTypes_AndKeepTheInnerException()
    {
        var inner = new IOException("disk");

        Assert.IsType<InvalidOperationException>(UserError.InvalidOperation(inner, "ErrorUnexpected", "x"));
        Assert.Same(inner, UserError.InvalidOperation(inner, "ErrorUnexpected", "x").InnerException);
        Assert.IsType<InvalidDataException>(UserError.InvalidData("ErrorUnexpected", "x"));
        Assert.Same(inner, UserError.InvalidData(inner, "ErrorUnexpected", "x").InnerException);
        Assert.IsType<IOException>(UserError.IO("ErrorUnexpected", "x"));
    }

    [Fact]
    public void FromWorker_RebuildsTheKeyThatCrossedTheProcessBoundary()
    {
        var tagged = UserError.FromWorker("ErrorUnexpected", ["boom"], "Unexpected error (boom)");
        var untagged = UserError.FromWorker(null, null, "plain message");

        Assert.True(UserError.TryGetKey(tagged, out var key, out var args));
        Assert.Equal("ErrorUnexpected", key);
        Assert.Equal(new object?[] { "boom" }, args);
        Assert.False(UserError.TryGetKey(untagged, out _, out _));
        Assert.Equal("plain message", untagged.Message);
    }

    [Fact]
    public void ToTextArgs_ConvertsArgumentsWithTheInvariantCulture()
    {
        Assert.Equal(new[] { "1.5", "", "texto", "42" }, UserError.ToTextArgs([1.5, null, "texto", 42]));
    }

    [Fact]
    public void PlainText_ConvertsToAMessageWithoutKey()
    {
        UserMessage message = "se cerro la sesion";

        Assert.Null(message.Key);
        Assert.Equal("se cerro la sesion", message.Localize());
    }
}
