using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LoopbackTranscriber.Interop;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.Views;

public partial class TrayMenuWindow : Window
{
    private const double OuterMargin = 14;
    private const double PointerGap = 8;
    private const int IconColumnWidth = 26;

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
        _openForeground = User32.GetForegroundWindow();

        User32.SetWindowLong(handle, User32.GwlExStyle,
            User32.GetWindowLong(handle, User32.GwlExStyle) | User32.WsExNoActivate | User32.WsExToolWindow);

        Opacity = 0;
        User32.SetWindowPos(handle, User32.HwndTopMost, cursorX, cursorY, 0, 0, User32.SwpNoSize | User32.SwpNoActivate);
        Show();
        UpdateLayout();

        var dpi = User32.GetDpiForWindow(handle);
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var margin = (int)Math.Round(OuterMargin * scale);
        var gap = (int)Math.Round(PointerGap * scale);

        var (x, y) = TrayMenuPlacement.Compute(cursorX, cursorY, width, height, margin, gap, GetWorkArea(cursorX, cursorY));

        User32.SetWindowPos(handle, User32.HwndTopMost, x, y, 0, 0, User32.SwpNoSize | User32.SwpNoActivate);
        Opacity = 1;

        _bodyMargin = margin;
        InstallMouseHook();
    }

    private void InstallMouseHook()
    {
        try
        {
            _mouseProc = MouseHookProc;
            _mouseHook = User32.SetWindowsHookEx(User32.WhMouseLl, _mouseProc, User32.GetModuleHandle(null), 0);
        }
        catch (Exception ex)
        {
            AppLog.Warn("TrayMenuWindow.InstallMouseHook: could not watch for clicks outside the menu", ex);
        }
    }

    private void RemoveMouseHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            User32.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _mouseProc = null;
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = (int)wParam;

            if (message is User32.WmLButtonDown or User32.WmRButtonDown or User32.WmMButtonDown or User32.WmXButtonDown)
            {
                var data = Marshal.PtrToStructure<MouseHookData>(lParam);

                if (!IsInsideMenu(data.Point.X, data.Point.Y))
                {
                    Dispatcher.BeginInvoke(new Action(CloseMenu));
                }
            }
        }

        return User32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool IsInsideMenu(int x, int y)
    {
        if (_handle == IntPtr.Zero || !User32.GetWindowRect(_handle, out var rect))
        {
            return false;
        }

        return x >= rect.Left + _bodyMargin && x < rect.Right - _bodyMargin
            && y >= rect.Top + _bodyMargin && y < rect.Bottom - _bodyMargin;
    }

    private void OnLiveTimerTick(object? sender, EventArgs e)
    {
        var foreground = User32.GetForegroundWindow();
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

        if (entry.Icon is TrayMenuEntry.RecordGlyph or TrayMenuEntry.StopGlyph)
        {
            return CreateRecordGlyph(isStop: entry.Icon == TrayMenuEntry.StopGlyph);
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

    private static ScreenArea GetWorkArea(int x, int y)
    {
        var monitor = User32.MonitorFromPoint(new NativePoint { X = x, Y = y }, User32.MonitorDefaultToNearest);
        var info = new MonitorInfo { cbSize = (uint)Marshal.SizeOf<MonitorInfo>() };

        if (monitor != IntPtr.Zero && User32.GetMonitorInfo(monitor, ref info))
        {
            return new ScreenArea(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
        }

        return new ScreenArea(0, 0, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
    }
}
