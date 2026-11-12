using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TestEngineer.Desktop.Controls;

/// <summary>
/// 嵌入 scrcpy（SetParent）。
/// 宿主 HWND 对齐到本控件在屏幕上的矩形（全屏/还原时 WPF 经常不改这个 HWND）。
/// 子窗口按视频宽高比 letterbox 后居中，窗口比等于画面比，避免拉伸，也避免 SDL 把画面留在左上角。
/// 缩放/拖边界时客户区可能短暂为 0：跳过无效尺寸并在恢复后补同步，防止画面消失。
/// </summary>
public sealed class ScrcpyHwndHost : HwndHost
{
    private const int MinHostPx = 16;

    private IntPtr _child;
    private IntPtr _host;
    private IntPtr _bgBrush;
    private double _aspectRatio; // 宽/高；<=0 时暂铺满
    private DispatcherTimer? _resizeDebounce;
    private DispatcherTimer? _recoveryTimer;
    private int _lastX = int.MinValue, _lastY = int.MinValue, _lastW, _lastH;
    private bool _pendingRecovery;
    private int _suspendCount;

    public IntPtr ChildHandle => _child;

    /// <summary>设备画面宽高比（宽÷高）。例如竖屏 0.45，横屏 2.2。</summary>
    public double AspectRatio
    {
        get => _aspectRatio;
        set
        {
            if (Math.Abs(_aspectRatio - value) < 0.0001)
            {
                return;
            }

            _aspectRatio = value;
            _lastX = int.MinValue;
            ForceSyncSize();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new HostAutomationPeer(this);

    private sealed class HostAutomationPeer : FrameworkElementAutomationPeer
    {
        public HostAutomationPeer(ScrcpyHwndHost owner) : base(owner) { }

        protected override List<AutomationPeer> GetChildrenCore() => [];

        protected override bool IsControlElementCore() => false;

        protected override bool IsContentElementCore() => false;
    }

    public void Attach(IntPtr childHwnd)
    {
        if (childHwnd == IntPtr.Zero || !IsWindow(childHwnd))
        {
            Detach();
            return;
        }

        if (_host == IntPtr.Zero)
        {
            _child = childHwnd;
            return;
        }

        if (_child == childHwnd && IsWindow(_child))
        {
            EnsureEmbedded();
            ForceSyncSize();
            return;
        }

        SoftReleaseChild();
        _child = childHwnd;
        Embed(_child);
        ForceSyncSize();
    }

    /// <summary>断开嵌入。默认不把子窗弹成独立顶层窗（避免拖窗尺寸时闪一下再丢）。</summary>
    public void Detach(bool showAsTopLevel = false)
    {
        SoftReleaseChild(showAsTopLevel);
        _lastX = int.MinValue;
        _pendingRecovery = false;
        _recoveryTimer?.Stop();
    }

    public void ForceSyncSize()
    {
        _resizeDebounce?.Stop();
        PlaceHostOverElement();
        ResizeChild();
    }

    /// <summary>模态对话框期间藏起嵌入画面，避免子 HWND 盖住对话框。</summary>
    public void SuspendAirspace()
    {
        _suspendCount++;
        HideChild();
    }

    public void ResumeAirspace()
    {
        if (_suspendCount == 0)
        {
            return;
        }

        _suspendCount--;
        if (_suspendCount == 0)
        {
            ForceSyncSize();
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _host = CreateWindowEx(
            0,
            "static",
            "",
            WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            0,
            0,
            Math.Max(1, (int)ActualWidth),
            Math.Max(1, (int)ActualHeight),
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        _bgBrush = CreateSolidBrush(0x00140E0A);
        InvalidateRect(_host, IntPtr.Zero, true);

        if (_child != IntPtr.Zero && IsWindow(_child))
        {
            Embed(_child);
            ForceSyncSize();
        }

        return new HandleRef(this, _host);
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmEraseBkgnd && _bgBrush != IntPtr.Zero)
        {
            GetClientRect(hwnd, out var rc);
            FillRect(wParam, ref rc, _bgBrush);
            handled = true;
            return (IntPtr)1;
        }

        if (msg is WmSize or WmWindowPosChanged)
        {
            ScheduleResize();
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    /// <summary>WPF 移动宿主后立刻按客户区重排子窗口，避免全屏后仍用旧尺寸。</summary>
    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        ResizeChild();
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _resizeDebounce?.Stop();
        _recoveryTimer?.Stop();
        // 宿主销毁时不要 ShowWindow 顶层，避免 scrcpy 被拽出再因进程状态异常消失
        SoftReleaseChild(showAsTopLevel: false);
        DestroyWindow(hwnd.Handle);
        _host = IntPtr.Zero;
        if (_bgBrush != IntPtr.Zero)
        {
            DeleteObject(_bgBrush);
            _bgBrush = IntPtr.Zero;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        PlaceHostOverElement(arranged);
        ResizeChild();
        return arranged;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ScheduleResize();
    }

    /// <summary>
    /// 把宿主 HWND 拉到控件当前的屏幕矩形。
    /// 全屏/退出全屏时 HwndHost 经常不更新这个窗口，子画面就停在原来的左上角。
    /// </summary>
    private void PlaceHostOverElement(Size? dipSize = null)
    {
        if (_host == IntPtr.Zero || !IsWindow(_host))
        {
            return;
        }

        if (PresentationSource.FromVisual(this) is not { CompositionTarget: { } target })
        {
            return;
        }

        var width = dipSize?.Width ?? (RenderSize.Width >= 1 ? RenderSize.Width : ActualWidth);
        var height = dipSize?.Height ?? (RenderSize.Height >= 1 ? RenderSize.Height : ActualHeight);
        if (width < 1 || height < 1)
        {
            return;
        }

        var device = target.TransformToDevice;
        var sx = device.M11 > 0.5 ? device.M11 : 1d;
        var sy = device.M22 > 0.5 ? device.M22 : 1d;
        var w = Math.Max(1, (int)Math.Round(width * sx));
        var h = Math.Max(1, (int)Math.Round(height * sy));

        Point screen;
        try
        {
            screen = PointToScreen(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var left = (int)Math.Round(screen.X);
        var top = (int)Math.Round(screen.Y);
        if (GetWindowRect(_host, out var hostRect))
        {
            var samePlace = Math.Abs(hostRect.Left - left) <= 2
                            && Math.Abs(hostRect.Top - top) <= 2
                            && Math.Abs(hostRect.Right - hostRect.Left - w) <= 2
                            && Math.Abs(hostRect.Bottom - hostRect.Top - h) <= 2;
            if (samePlace)
            {
                return;
            }
        }

        var parent = GetParent(_host);
        var origin = new POINT { X = left, Y = top };
        if (parent == IntPtr.Zero || !ScreenToClient(parent, ref origin))
        {
            SetWindowPos(_host, IntPtr.Zero, 0, 0, w, h, SwpNoZOrder | SwpNoActivate | SwpNoMove | SwpShowWindow);
            return;
        }

        SetWindowPos(_host, IntPtr.Zero, origin.X, origin.Y, w, h, SwpNoZOrder | SwpNoActivate | SwpShowWindow);
    }

    private void ScheduleResize()
    {
        _resizeDebounce ??= new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(40),
        };
        _resizeDebounce.Tick -= OnResizeDebounceTick;
        _resizeDebounce.Tick += OnResizeDebounceTick;
        _resizeDebounce.Stop();
        _resizeDebounce.Start();
    }

    private void OnResizeDebounceTick(object? sender, EventArgs e)
    {
        _resizeDebounce?.Stop();
        PlaceHostOverElement();
        ResizeChild();
    }

    private void SoftReleaseChild(bool showAsTopLevel = false)
    {
        if (_child == IntPtr.Zero)
        {
            return;
        }

        if (IsWindow(_child))
        {
            if (!showAsTopLevel)
            {
                ShowWindow(_child, SwHide);
            }

            SetParent(_child, IntPtr.Zero);
            if (showAsTopLevel)
            {
                ShowWindow(_child, SwShow);
            }
        }

        _child = IntPtr.Zero;
    }

    private void Embed(IntPtr child)
    {
        if (!IsWindow(child) || _host == IntPtr.Zero || !IsWindow(_host))
        {
            return;
        }

        SetParent(child, _host);
        var style = GetWindowLong(child, GwlStyle);
        style = (style | WsChild | WsVisible | WsClipSiblings)
                & ~WsPopup & ~WsCaption & ~WsThickFrame & ~WsMinimizeBox & ~WsMaximizeBox & ~WsSysMenu;
        _ = SetWindowLong(child, GwlStyle, style);
        // 去掉可能残留的顶层扩展样式
        var ex = GetWindowLong(child, GwlExStyle);
        ex &= ~(WsExDlgModalFrame | WsExClientEdge | WsExStaticEdge | WsExAppWindow | WsExToolWindow);
        _ = SetWindowLong(child, GwlExStyle, ex);
        var show = _suspendCount > 0 ? 0u : SwpShowWindow;
        ShowWindow(child, _suspendCount > 0 ? SwHide : SwShow);
        SetWindowPos(child, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged | show);
    }

    private void EnsureEmbedded()
    {
        if (_child == IntPtr.Zero || _host == IntPtr.Zero)
        {
            return;
        }

        if (!IsWindow(_child) || !IsWindow(_host))
        {
            return;
        }

        var parent = GetParent(_child);
        if (parent != _host)
        {
            Embed(_child);
            _lastX = int.MinValue; // 强制下一次 Move
        }
    }

    private void HideChild()
    {
        if (_child != IntPtr.Zero && IsWindow(_child))
        {
            ShowWindow(_child, SwHide);
        }
    }

    private void ResizeChild()
    {
        if (_suspendCount > 0)
        {
            HideChild();
            return;
        }

        if (_host == IntPtr.Zero || !IsWindow(_host))
        {
            return;
        }

        if (!GetClientRect(_host, out var rc))
        {
            return;
        }

        var hostW = Math.Max(0, rc.Right - rc.Left);
        var hostH = Math.Max(0, rc.Bottom - rc.Top);

        // 拖动边界/布局切换时客户区常短暂为 0；此时切勿把 scrcpy 缩成 1×1，否则易黑屏卡死
        if (hostW < MinHostPx || hostH < MinHostPx)
        {
            ScheduleRecovery();
            return;
        }

        if (_child == IntPtr.Zero || !IsWindow(_child))
        {
            return;
        }

        EnsureEmbedded();

        int x = 0, y = 0, w = hostW, h = hostH;
        if (_aspectRatio > 0.05)
        {
            var hostAspect = (double)hostW / hostH;
            if (hostAspect > _aspectRatio)
            {
                h = hostH;
                w = Math.Max(1, (int)Math.Round(hostH * _aspectRatio));
            }
            else
            {
                w = hostW;
                h = Math.Max(1, (int)Math.Round(hostW / _aspectRatio));
            }

            w = Math.Min(w, hostW);
            h = Math.Min(h, hostH);
            x = Math.Max(0, (hostW - w) / 2);
            y = Math.Max(0, (hostH - h) / 2);
        }

        if (IsChildPlaced(x, y, w, h))
        {
            _lastX = x;
            _lastY = y;
            _lastW = w;
            _lastH = h;
            ShowWindow(_child, SwShow);
            return;
        }

        var prevW = _lastW;
        var prevH = _lastH;
        _lastX = x;
        _lastY = y;
        _lastW = w;
        _lastH = h;

        // 跨度很大（全屏/退出全屏）时先改到相邻尺寸，SDL 才会丢掉缓存的视口，否则画面仍停在左上角。
        var bigJump = prevW > MinHostPx && prevH > MinHostPx
                      && (Math.Abs(w - prevW) > 32 || Math.Abs(h - prevH) > 32);
        if (bigJump)
        {
            MoveWindow(_child, x, y, Math.Max(1, w - 1), Math.Max(1, h - 1), false);
        }

        MoveWindow(_child, x, y, w, h, true);
        var packed = (IntPtr)unchecked((int)(((uint)h << 16) | ((uint)w & 0xFFFF)));
        SendMessage(_child, WmSize, IntPtr.Zero, packed);
        ShowWindow(_child, SwShow);
        InvalidateRect(_host, IntPtr.Zero, true);
        InvalidateRect(_child, IntPtr.Zero, true);
        _pendingRecovery = false;
        _recoveryTimer?.Stop();
    }

    private bool IsChildPlaced(int x, int y, int w, int h)
    {
        if (!GetWindowRect(_child, out var wr))
        {
            return false;
        }

        var origin = new POINT { X = wr.Left, Y = wr.Top };
        if (!ScreenToClient(_host, ref origin))
        {
            return false;
        }

        return Math.Abs(origin.X - x) <= 2
               && Math.Abs(origin.Y - y) <= 2
               && Math.Abs(wr.Right - wr.Left - w) <= 2
               && Math.Abs(wr.Bottom - wr.Top - h) <= 2;
    }

    private void ScheduleRecovery()
    {
        _pendingRecovery = true;
        _recoveryTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _recoveryTimer.Tick -= OnRecoveryTick;
        _recoveryTimer.Tick += OnRecoveryTick;
        _recoveryTimer.Stop();
        _recoveryTimer.Start();
    }

    private void OnRecoveryTick(object? sender, EventArgs e)
    {
        if (!_pendingRecovery)
        {
            _recoveryTimer?.Stop();
            return;
        }

        ForceSyncSize();
        if (!_pendingRecovery)
        {
            _recoveryTimer?.Stop();
        }
    }

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsMaximizeBox = 0x00010000;
    private const int WsSysMenu = 0x00080000;
    private const int WsExDlgModalFrame = 0x00000001;
    private const int WsExClientEdge = 0x00000200;
    private const int WsExStaticEdge = 0x00020000;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExToolWindow = 0x00000080;
    private const int SwShow = 5;
    private const int SwHide = 0;
    private const int WmEraseBkgnd = 0x0014;
    private const int WmSize = 0x0005;
    private const int WmWindowPosChanged = 0x0047;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int crColor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
