using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LoopbackTranscriber.Services;

public static class SystemUserName
{
    public const int MaxLength = 20;

    private const int NameDisplay = 3;

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool GetUserNameEx(int nameFormat, StringBuilder userName, ref uint userNameSize);

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
        catch (Exception ex)
        {
            AppLog.Warn("SystemUserName.Detect: no se pudo obtener el nombre del usuario del equipo", ex);
        }

        return string.Empty;
    }

    private static string GetDisplayName()
    {
        var buffer = new StringBuilder(256);
        var size = (uint)buffer.Capacity;

        return GetUserNameEx(NameDisplay, buffer, ref size) ? buffer.ToString() : string.Empty;
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

        var firstName = name.Split(new[] { ' ', '.', '_' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        return firstName.Length > MaxLength ? firstName[..MaxLength] : firstName;
    }
}
