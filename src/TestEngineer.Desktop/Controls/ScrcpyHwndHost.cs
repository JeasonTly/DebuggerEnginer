using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TestEngineer.Desktop.Controls;

/// <summary>把外部 HWND（scrcpy）嵌入 WPF，随布局 Resize。</summary>
public sealed class ScrcpyHwndHost : HwndHost
{
    private IntPtr _child;
    private IntPtr _host;
    private IntPtr _bgBrush;

    public IntPtr ChildHandle => _child;

    public void Attach(IntPtr childHwnd)
    {
        if (childHwnd == IntPtr.Zero)
        {
            Detach();
            return;
        }

        if (_host == IntPtr.Zero)
        {
            _child = childHwnd;
            return;
        }

        Detach();
        _child = childHwnd;
        Embed(_child);
        ResizeChild();
    }

    public void Detach()
    {
        if (_child != IntPtr.Zero)
        {
            SetParent(_child, IntPtr.Zero);
            ShowWindow(_child, SwShow);
            _child = IntPtr.Zero;
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _host = CreateWindowEx(
            0,
            "static",
            "",
            WsChild | WsVisible,
            0,
            0,
            Math.Max(1, (int)ActualWidth),
            Math.Max(1, (int)ActualHeight),
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        // #0A0E14 → COLORREF 0x00140E0A；避免未嵌入时白底透出
        _bgBrush = CreateSolidBrush(0x00140E0A);
        SetWindowLongPtr(_host, GwlpUserData, _bgBrush);
        InvalidateRect(_host, IntPtr.Zero, true);

        if (_child != IntPtr.Zero)
        {
            Embed(_child);
        }

        return new HandleRef(this, _host);
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((msg == WmEraseBkgnd || msg == WmPaint) && _child == IntPtr.Zero && _bgBrush != IntPtr.Zero)
        {
            var hdc = msg == WmEraseBkgnd ? wParam : GetDC(hwnd);
            if (hdc != IntPtr.Zero)
            {
                GetClientRect(hwnd, out var rc);
                FillRect(hdc, ref rc, _bgBrush);
                if (msg == WmPaint)
                {
                    ReleaseDC(hwnd, hdc);
                    ValidateRect(hwnd, IntPtr.Zero);
                }

                handled = true;
                return msg == WmEraseBkgnd ? (IntPtr)1 : IntPtr.Zero;
            }
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Detach();
        DestroyWindow(hwnd.Handle);
        _host = IntPtr.Zero;
        if (_bgBrush != IntPtr.Zero)
        {
            DeleteObject(_bgBrush);
            _bgBrush = IntPtr.Zero;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ResizeChild();
    }

    private void Embed(IntPtr child)
    {
        SetParent(child, _host);
        var style = GetWindowLong(child, GwlStyle);
        style = (style | WsChild | WsVisible) & ~WsPopup & ~WsCaption & ~WsThickFrame & ~WsMinimizeBox & ~WsMaximizeBox & ~WsSysMenu;
        _ = SetWindowLong(child, GwlStyle, style);
        ShowWindow(child, SwShow);
    }

    private void ResizeChild()
    {
        if (_child == IntPtr.Zero || _host == IntPtr.Zero)
        {
            return;
        }

        var w = Math.Max(1, (int)ActualWidth);
        var h = Math.Max(1, (int)ActualHeight);
        MoveWindow(_host, 0, 0, w, h, true);
        MoveWindow(_child, 0, 0, w, h, true);
    }

    private const int GwlStyle = -16;
    private const int GwlpUserData = -21;
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsMaximizeBox = 0x00010000;
    private const int WsSysMenu = 0x00080000;
    private const int SwShow = 5;
    private const int WmEraseBkgnd = 0x0014;
    private const int WmPaint = 0x000F;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
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
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern bool ValidateRect(IntPtr hWnd, IntPtr lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int crColor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
