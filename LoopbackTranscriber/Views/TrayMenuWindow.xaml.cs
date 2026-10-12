using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.Views;

public partial class TrayMenuWindow : Window
{
    public const string RecordGlyph = "glyph:record";
    public const string StopGlyph = "glyph:stop";

    private const double OuterMargin = 14;
    private const int IconColumnWidth = 26;
    private const int MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoActivate = 0x10;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x201;
    private const int WmRButtonDown = 0x204;
    private const int WmMButtonDown = 0x207;
    private const int WmXButtonDown = 0x20B;

    private static readonly Brush RecordingBackground = CreateFrozenBrush(Color.FromArgb(0x30, 0xE5, 0x39, 0x35));
    private static readonly Brush RecordingDot = CreateFrozenBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly Brush RecordingTime = CreateFrozenBrush(Color.FromRgb(0xFF, 0x6B, 0x66));

    private readonly DispatcherTimer _liveTimer;
    private readonly List<(TextBlock Block, Func<string> Provider)> _liveBlocks = [];

    private LowLevelMouseProc? _mouseProc;
    private IntPtr _mouseHook;
    private IntPtr _handle;
    private IntPtr _openForeground;
    private int _bodyMargin;
    private Action? _pendingAction;
    private bool _closing;

    public TrayMenuWindow(IReadOnlyList<TrayMenuEntry> entries)
    {
        InitializeComponent();

        foreach (var entry in entries)
        {
            ItemsHost.Children.Add(CreateRow(entry));
        }

        _liveTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        _liveTimer.Tick += OnLiveTimerTick;
        _liveTimer.Start();

        Closed += (_, _) =>
        {
            _liveTimer.Stop();
            _liveTimer.Tick -= OnLiveTimerTick;
            RemoveMouseHook();

            var action = _pendingAction;
            if (action is not null)
            {
                Dispatcher.BeginInvoke(action);
            }
        };
    }

    public void CloseMenu()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Close();
    }

    public void ShowAt(int cursorX, int cursorY)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        _handle = handle;
        _openForeground = GetForegroundWindow();

        SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExNoActivate | WsExToolWindow);

        Opacity = 0;
        SetWindowPos(handle, new IntPtr(-1), cursorX, cursorY, 0, 0, SwpNoSize | SwpNoActivate);
        Show();
        UpdateLayout();

        var dpi = GetDpiForWindow(handle);
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var margin = (int)Math.Round(OuterMargin * scale);
        var gap = (int)Math.Round(8 * scale);

        var work = GetWorkArea(cursorX, cursorY);

        var x = cursorX - width / 2;
        x = Math.Min(x, work.Right - width + margin - gap);
        x = Math.Max(x, work.Left - margin + gap);

        int y;
        if (cursorY > (work.Top + work.Bottom) / 2)
        {
            y = Math.Min(cursorY, work.Bottom) - height + margin - gap;
        }
        else
        {
            y = Math.Max(cursorY, work.Top) - margin + gap;
        }

        y = Math.Min(y, work.Bottom - height + margin - gap);
        y = Math.Max(y, work.Top - margin + gap);

        SetWindowPos(handle, new IntPtr(-1), x, y, 0, 0, SwpNoSize | SwpNoActivate);
        Opacity = 1;

        _bodyMargin = margin;
        InstallMouseHook();
    }

    private void InstallMouseHook()
    {
        try
        {
            _mouseProc = MouseHookProc;
            _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, GetModuleHandle(null), 0);
        }
        catch (Exception ex)
        {
            AppLog.Warn("TrayMenuWindow.InstallMouseHook: no se pudo vigilar los clics fuera del menu", ex);
        }
    }

    private void RemoveMouseHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _mouseProc = null;
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = (int)wParam;

            if (message is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown)
            {
                var data = Marshal.PtrToStructure<MouseHookData>(lParam);

                if (!IsInsideMenu(data.Point.X, data.Point.Y))
                {
                    Dispatcher.BeginInvoke(new Action(CloseMenu));
                }
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool IsInsideMenu(int x, int y)
    {
        if (_handle == IntPtr.Zero || !GetWindowRect(_handle, out var rect))
        {
            return false;
        }

        return x >= rect.Left + _bodyMargin && x < rect.Right - _bodyMargin
            && y >= rect.Top + _bodyMargin && y < rect.Bottom - _bodyMargin;
    }

    private void OnLiveTimerTick(object? sender, EventArgs e)
    {
        var foreground = GetForegroundWindow();
        if (foreground != _openForeground && foreground != _handle)
        {
            CloseMenu();
            return;
        }

        foreach (var (block, provider) in _liveBlocks)
        {
            block.Text = provider();
        }
    }

    private UIElement CreateRow(TrayMenuEntry entry)
    {
        if (entry.IsSeparator)
        {
            var line = new Border { Height = 1, Margin = new Thickness(8, 4, 8, 4) };
            line.SetResourceReference(Border.BackgroundProperty, "ControlStrokeColorDefaultBrush");
            return line;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconCell = CreateIconCell(entry);
        if (iconCell is not null)
        {
            Grid.SetColumn(iconCell, 0);
            grid.Children.Add(iconCell);
        }

        var text = new TextBlock
        {
            Text = entry.Text,
            FontSize = 13,
            FontWeight = entry.IsDefault || entry.IsStatus ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, entry.LiveText is null ? 0 : 16, 0)
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (entry.LiveText is not null)
        {
            var live = new TextBlock
            {
                Text = entry.LiveText(),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (entry.IsStatus)
            {
                live.Foreground = RecordingTime;
            }
            else
            {
                live.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            }

            Grid.SetColumn(live, 2);
            grid.Children.Add(live);
            _liveBlocks.Add((live, entry.LiveText));
        }

        var row = new Border { Child = grid };
        var interactive = entry.Enabled && !entry.IsStatus && entry.Invoke is not null;

        if (interactive)
        {
            row.SetResourceReference(StyleProperty, "TrayItemStyle");
            row.MouseLeftButtonUp += (_, _) =>
            {
                _pendingAction = entry.Invoke;
                CloseMenu();
            };
        }
        else
        {
            row.CornerRadius = new CornerRadius(6);
            row.Padding = new Thickness(10, 8, 10, 8);
            row.Margin = new Thickness(0, 1, 0, 1);
            row.IsHitTestVisible = false;

            if (entry.IsStatus)
            {
                row.Background = RecordingBackground;
            }
            else
            {
                row.Opacity = 0.5;
            }
        }

        return row;
    }

    private static UIElement? CreateIconCell(TrayMenuEntry entry)
    {
        if (entry.IsStatus)
        {
            return new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = RecordingDot,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0)
            };
        }

        if (entry.Icon is RecordGlyph or StopGlyph)
        {
            return CreateRecordGlyph(isStop: entry.Icon == StopGlyph);
        }

        if (entry.Icon is not null && Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(entry.Icon, out var symbol))
        {
            var icon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = symbol,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            icon.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
            return icon;
        }

        return null;
    }

    private static UIElement CreateRecordGlyph(bool isStop)
    {
        var glyph = new Grid
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        var ring = new Ellipse { StrokeThickness = 1.5 };
        ring.SetResourceReference(Shape.StrokeProperty, "TextFillColorPrimaryBrush");
        glyph.Children.Add(ring);

        if (isStop)
        {
            var square = new Rectangle
            {
                Width = 6,
                Height = 6,
                RadiusX = 1,
                RadiusY = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            square.SetResourceReference(Shape.FillProperty, "TextFillColorPrimaryBrush");
            glyph.Children.Add(square);
        }
        else
        {
            glyph.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = RecordingDot,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        return glyph;
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static NativeRect GetWorkArea(int x, int y)
    {
        var monitor = MonitorFromPoint(new NativePoint { X = x, Y = y }, MonitorDefaultToNearest);
        var info = new MonitorInfo { cbSize = (uint)Marshal.SizeOf<MonitorInfo>() };

        return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info)
            ? info.rcWork
            : new NativeRect { Left = 0, Top = 0, Right = (int)SystemParameters.PrimaryScreenWidth, Bottom = (int)SystemParameters.PrimaryScreenHeight };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);
}
