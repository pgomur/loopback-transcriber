namespace LoopbackTranscriber.Tests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InterfaceLanguageTestGroup
{
    public const string Name = "Interface language";
}

public static class InterfaceLanguage
{
    public const string Default = "es";

    public static IDisposable Use(string language)
    {
        Strings.Instance.SetLanguage(language);
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => Strings.Instance.SetLanguage(Default);
    }
}
