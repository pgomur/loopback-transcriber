using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LoopbackTranscriber.Services;

public sealed class TrayIconService : IDisposable
{
    private const int WmApp = 0x8000;
    private const int WmTrayCallback = WmApp + 1;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDoubleClick = 0x0203;
    private const int WmRButtonUp = 0x0205;

    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;

    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;
    private const uint NifInfo = 0x10;
    private const uint NiifInfo = 0x1;

    private const uint WsPopup = 0x80000000;
    private const int SmCxSmIcon = 49;
    private const int IdiApplication = 32512;
    private const uint TrayIconId = 1;

    private readonly Action _showRequested;
    private readonly Action<int, int> _contextMenuRequested;
    private readonly HwndSource _source;
    private readonly uint _taskbarCreatedMessage;
    private readonly IntPtr _iconHandle;
    private readonly bool _ownsIcon;

    private IntPtr _currentIcon;
    private IntPtr _recordingIcon;
    private bool _recording;

    private string _tooltip;
    private bool _wantVisible;
    private bool _disposed;

    public TrayIconService(Action showRequested, Action<int, int> contextMenuRequested, string tooltip)
    {
        _showRequested = showRequested;
        _contextMenuRequested = contextMenuRequested;
        _tooltip = tooltip;

        var parameters = new HwndSourceParameters("LoopbackTranscriberTray")
        {
            WindowStyle = unchecked((int)WsPopup),
            Width = 0,
            Height = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        (_iconHandle, _ownsIcon) = LoadTrayIcon();
        _currentIcon = _iconHandle;
    }

    public void Show()
    {
        if (_disposed || _wantVisible)
        {
            return;
        }

        _wantVisible = true;
        Notify(NimAdd, NifMessage | NifIcon | NifTip);
        UpdateRecordingAnimation();
    }

    public void Hide()
    {
        if (_disposed || !_wantVisible)
        {
            return;
        }

        _wantVisible = false;
        Notify(NimDelete, 0);
    }

    public void SetRecording(bool recording)
    {
        if (_disposed || _recording == recording)
        {
            return;
        }

        _recording = recording;

        if (recording)
        {
            EnsureRecordingIcon();
        }

        UpdateRecordingAnimation();
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;

        if (_wantVisible && !_disposed)
        {
            Notify(NimModify, NifMessage | NifIcon | NifTip);
        }
    }

    public void ShowBalloon(string title, string body)
    {
        if (_wantVisible && !_disposed)
        {
            Notify(NimModify, NifMessage | NifIcon | NifTip | NifInfo, title, body);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Hide();
        _disposed = true;

        DestroyRecordingIcon();

        _source.RemoveHook(WndProc);
        _source.Dispose();

        if (_ownsIcon && _iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
        }
    }

    private bool Notify(uint message, uint flags, string? balloonTitle = null, string? balloonBody = null)
    {
        var data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _source.Handle,
            uID = TrayIconId,
            uFlags = flags,
            uCallbackMessage = WmTrayCallback,
            hIcon = _currentIcon,
            szTip = Truncate(_tooltip, 127),
            szInfo = Truncate(balloonBody ?? string.Empty, 255),
            szInfoTitle = Truncate(balloonTitle ?? string.Empty, 63),
            dwInfoFlags = NiifInfo
        };

        return Shell_NotifyIcon(message, ref data);
    }

    private void EnsureRecordingIcon()
    {
        if (_recordingIcon != IntPtr.Zero)
        {
            return;
        }

        try
        {
            var size = GetSystemMetrics(SmCxSmIcon);
            if (size <= 0)
            {
                size = 16;
            }

            _recordingIcon = CreateRecordingIcon(size);
        }
        catch (Exception ex)
        {
            AppLog.Warn("TrayIconService.EnsureRecordingIcon: could not create the recording icon", ex);
        }
    }

    private void DestroyRecordingIcon()
    {
        if (_recordingIcon == IntPtr.Zero)
        {
            return;
        }

        DestroyIcon(_recordingIcon);
        _recordingIcon = IntPtr.Zero;
    }

    private void UpdateRecordingAnimation()
    {
        if (_disposed)
        {
            return;
        }

        ApplyIcon(_recording && _recordingIcon != IntPtr.Zero ? _recordingIcon : _iconHandle);
    }

    private void ApplyIcon(IntPtr icon)
    {
        if (_currentIcon == icon)
        {
            return;
        }

        _currentIcon = icon;

        if (_wantVisible && !_disposed)
        {
            Notify(NimModify, NifMessage | NifIcon | NifTip);
        }
    }

    private static IntPtr CreateRecordingIcon(int size)
    {
        var decoder = BitmapDecoder.Create(
            new Uri("pack://application:,,,/Assets/AppIcon.ico"),
            BitmapCreateOptions.None,
            BitmapCacheOption.OnLoad);

        var baseFrame = decoder.Frames
                            .Where(f => f.PixelWidth >= size)
                            .OrderBy(f => f.PixelWidth)
                            .FirstOrDefault()
                        ?? decoder.Frames.OrderByDescending(f => f.PixelWidth).First();

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);

        using (var context = visual.RenderOpen())
        {
            context.DrawImage(baseFrame, new Rect(0, 0, size, size));

            var radius = size * 0.17;
            var margin = size * 0.06;
            var center = new Point(size - radius - margin, size - radius - margin);
            var ringRadius = radius + Math.Max(0.75, size * 0.04);

            var red = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
            red.Freeze();

            context.DrawEllipse(Brushes.White, null, center, ringRadius, ringRadius);
            context.DrawEllipse(red, null, center, radius, radius);
        }

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var straight = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[size * size * 4];
        straight.CopyPixels(pixels, size * 4, 0);

        return CreateIconFromPixels(pixels, size);
    }

    private static IntPtr CreateIconFromPixels(byte[] pixels, int size)
    {
        var header = new BitmapInfoHeader
        {
            biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            biWidth = size,
            biHeight = -size,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0
        };

        var screenDc = GetDC(IntPtr.Zero);
        var colorBitmap = CreateDIBSection(screenDc, ref header, 0, out var bits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, screenDc);

        if (colorBitmap == IntPtr.Zero || bits == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        Marshal.Copy(pixels, 0, bits, pixels.Length);

        var maskBits = new byte[((size + 15) / 16) * 2 * size];
        var maskBitmap = CreateBitmap(size, size, 1, 1, maskBits);

        var iconInfo = new IconInfo
        {
            fIcon = true,
            hbmMask = maskBitmap,
            hbmColor = colorBitmap
        };

        var icon = CreateIconIndirect(ref iconInfo);

        DeleteObject(colorBitmap);
        if (maskBitmap != IntPtr.Zero)
        {
            DeleteObject(maskBitmap);
        }

        return icon;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmTrayCallback)
        {
            var mouseMessage = (int)(lParam.ToInt64() & 0xFFFF);

            switch (mouseMessage)
            {
                case WmLButtonUp:
                case WmLButtonDoubleClick:
                    handled = true;
                    _source.Dispatcher.BeginInvoke(_showRequested);
                    break;

                case WmRButtonUp:
                    handled = true;
                    GetCursorPos(out var cursor);
                    _source.Dispatcher.BeginInvoke(new Action(() => _contextMenuRequested(cursor.X, cursor.Y)));
                    break;
            }
        }
        else if ((uint)msg == _taskbarCreatedMessage && _wantVisible)
        {
            Notify(NimAdd, NifMessage | NifIcon | NifTip);
        }

        return IntPtr.Zero;
    }

    private static (IntPtr Handle, bool Owned) LoadTrayIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var small = new IntPtr[1];
                if (ExtractIconEx(path, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                {
                    return (small[0], true);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("TrayIconService.LoadTrayIcon: could not extract the application icon", ex);
        }

        return (LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication)), false);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] : value;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIcon;

        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref IconInfo piconinfo);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, byte[]? lpBits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);
}
