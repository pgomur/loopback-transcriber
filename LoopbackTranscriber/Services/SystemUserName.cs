using System.Linq;
using System.Runtime.InteropServices;

namespace LoopbackTranscriber.Services;

public static class SystemUserName
{
    public const int MaxLength = 20;

    private const int NameDisplay = 3;

    private const int DisplayNameBufferLength = 256;

    private static readonly char[] NameSeparators = [' ', '.', '_'];

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool GetUserNameEx(int nameFormat, [Out] char[] userName, ref uint userNameSize);

    public static string Detect()
    {
        try
        {
            var fromDisplayName = Normalize(GetDisplayName());
            if (fromDisplayName.Length > 0)
            {
                return fromDisplayName;
            }

            var fromLogin = Normalize(Environment.UserName);
            if (fromLogin.Length > 0)
            {
                return char.ToUpperInvariant(fromLogin[0]) + fromLogin[1..];
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            AppLog.Warn("SystemUserName.Detect: could not get the computer's user name", ex);
        }

        return string.Empty;
    }

    private static string GetDisplayName()
    {
        var buffer = new char[DisplayNameBufferLength];
        var size = (uint)buffer.Length;

        return GetUserNameEx(NameDisplay, buffer, ref size)
            ? new string(buffer, 0, (int)Math.Min(size, (uint)buffer.Length))
            : string.Empty;
    }

    private static string Normalize(string? rawName)
    {
        var name = (rawName ?? string.Empty).Trim();

        var atIndex = name.IndexOf('@');
        if (atIndex > 0)
        {
            name = name[..atIndex];
        }

        var commaIndex = name.IndexOf(',');
        if (commaIndex >= 0)
        {
            name = name[(commaIndex + 1)..];
        }

        var firstName = name.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        return firstName.Length > MaxLength ? firstName[..MaxLength] : firstName;
    }
}
