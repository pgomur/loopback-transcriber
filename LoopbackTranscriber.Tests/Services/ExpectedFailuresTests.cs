using System.Runtime.InteropServices;

namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class ExpectedFailuresTests
{
    [Fact]
    public void IsFile_AcceptsOnlyDiskAndPermissionErrors()
    {
        Assert.True(ExpectedFailures.IsFile(new IOException()));
        Assert.True(ExpectedFailures.IsFile(new FileNotFoundException()));
        Assert.True(ExpectedFailures.IsFile(new DirectoryNotFoundException()));
        Assert.True(ExpectedFailures.IsFile(new UnauthorizedAccessException()));

        Assert.False(ExpectedFailures.IsFile(new InvalidOperationException()));
        Assert.False(ExpectedFailures.IsFile(new ArgumentNullException("value")));
        Assert.False(ExpectedFailures.IsFile(new FormatException()));
    }

    [Fact]
    public void IsAudioFile_AlsoAcceptsBrokenAudioFormats()
    {
        Assert.True(ExpectedFailures.IsAudioFile(new IOException()));
        Assert.True(ExpectedFailures.IsAudioFile(new FormatException()));
        Assert.True(ExpectedFailures.IsAudioFile(new InvalidDataException()));

        Assert.False(ExpectedFailures.IsAudioFile(new ArgumentOutOfRangeException()));
        Assert.False(ExpectedFailures.IsAudioFile(DeviceInvalidated()));
    }

    [Fact]
    public void IsDeviceOrFile_AlsoAcceptsAudioDeviceErrors()
    {
        Assert.True(ExpectedFailures.IsDeviceOrFile(DeviceInvalidated()));
        Assert.True(ExpectedFailures.IsDeviceOrFile(new InvalidComObjectException()));
        Assert.True(ExpectedFailures.IsDeviceOrFile(new UnauthorizedAccessException()));

        Assert.False(ExpectedFailures.IsDeviceOrFile(new FormatException()));
        Assert.False(ExpectedFailures.IsDeviceOrFile(new InvalidCastException()));
    }

    private static COMException DeviceInvalidated()
    {
        return Assert.IsType<COMException>(Marshal.GetExceptionForHR(unchecked((int)0x88890004)));
    }
}
