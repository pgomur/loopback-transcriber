using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;

namespace LoopbackTranscriber.Services;

public sealed record UserMessage(string? Key, object?[] Args, string Text)
{
    public static UserMessage FromKey(string key, params object?[] args) =>
        new(key, args, Strings.Instance.FormatIn("en", key, args));

    public static UserMessage From(Exception exception) =>
        UserError.TryGetKey(exception, out var key, out var args)
            ? new UserMessage(key, args, exception.Message)
            : FromKey(UserError.UnexpectedKey, exception.Message);

    public string Localize() => Key is null ? Text : Strings.Instance.Format(Key, Args);

    public static implicit operator UserMessage(string text) => new(null, Array.Empty<object?>(), text);
}

internal static class UserError
{
    private const string KeyName = "LoopbackTranscriber.MessageKey";
    private const string ArgsName = "LoopbackTranscriber.MessageArgs";

    public static InvalidOperationException InvalidOperation(string key, params object?[] args) =>
        Tag(new InvalidOperationException(English(key, args)), key, args);

    public static InvalidOperationException InvalidOperation(Exception? innerException, string key, params object?[] args) =>
        Tag(new InvalidOperationException(English(key, args), innerException), key, args);

    public static InvalidDataException InvalidData(string key, params object?[] args) =>
        Tag(new InvalidDataException(English(key, args)), key, args);

    public static InvalidDataException InvalidData(Exception? innerException, string key, params object?[] args) =>
        Tag(new InvalidDataException(English(key, args), innerException), key, args);

    public static IOException IO(string key, params object?[] args) =>
        Tag(new IOException(English(key, args)), key, args);

    public static InvalidOperationException FromWorker(string? key, string[]? args, string message) =>
        key is null
            ? new InvalidOperationException(message)
            : Tag(new InvalidOperationException(message), key, args ?? Array.Empty<object?>());

    public static bool TryGetKey(Exception exception, [NotNullWhen(true)] out string? key, out object?[] args)
    {
        if (exception.Data[KeyName] is string found)
        {
            key = found;
            args = exception.Data[ArgsName] as object?[] ?? Array.Empty<object?>();
            return true;
        }

        key = null;
        args = Array.Empty<object?>();
        return false;
    }

    public const string UnexpectedKey = "ErrorUnexpected";

    public static string ToUserMessage(this Exception exception) =>
        TryGetKey(exception, out var key, out var args)
            ? Strings.Instance.Format(key, args)
            : Strings.Instance.Format(UnexpectedKey, exception.Message);

    internal static string[] ToTextArgs(object?[] args) =>
        args.Select(a => a as string ?? Convert.ToString(a, CultureInfo.InvariantCulture) ?? string.Empty).ToArray();

    private static T Tag<T>(T exception, string key, object?[] args) where T : Exception
    {
        exception.Data[KeyName] = key;
        exception.Data[ArgsName] = args;
        return exception;
    }

    private static string English(string key, object?[] args) => Strings.Instance.FormatIn("en", key, args);
}
