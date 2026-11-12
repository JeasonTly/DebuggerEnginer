using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace TestEngineer.Desktop.Views;

/// <summary>鼠标松开时，读取指针下资源管理器窗口正在浏览的文件夹。</summary>
internal static class ExplorerDropFolder
{
    private const uint GaRoot = 2;

    public static string? TryGetFolderAtCursor()
    {
        if (!GetCursorPos(out var point))
        {
            return null;
        }

        var root = GetAncestor(WindowFromPoint(point), GaRoot);
        if (root == IntPtr.Zero)
        {
            return null;
        }

        var className = ClassName(root);
        if (className is "Progman" or "WorkerW")
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Directory.Exists(desktop) ? desktop : null;
        }

        return FindShellFolder(root);
    }

    private static string? FindShellFolder(IntPtr root)
    {
        object? shell = null;
        object? windows = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return null;
            }

            shell = Activator.CreateInstance(shellType);
            windows = shellType.InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null);
            if (windows is null)
            {
                return null;
            }

            var windowsType = windows.GetType();
            var count = Convert.ToInt32(windowsType.InvokeMember("Count", BindingFlags.GetProperty, null, windows, null), System.Globalization.CultureInfo.InvariantCulture);
            for (var i = 0; i < count; i++)
            {
                object? window = null;
                try
                {
                    window = windowsType.InvokeMember("Item", BindingFlags.InvokeMethod, null, windows, [i]);
                    if (window is null || !SameWindow(window, root))
                    {
                        continue;
                    }

                    var path = FolderPath(window);
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    {
                        return path;
                    }
                }
                catch (COMException)
                {
                    // 浏览器或控制面板窗口没有文件夹路径。
                }
                catch (TargetInvocationException ex) when (ex.InnerException is COMException)
                {
                }
                finally
                {
                    Release(window);
                }
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Release(windows);
            Release(shell);
        }

        return null;
    }

    private static bool SameWindow(object window, IntPtr root)
    {
        var raw = window.GetType().InvokeMember("HWND", BindingFlags.GetProperty, null, window, null);
        return raw is not null && Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture) == root.ToInt64();
    }

    private static string? FolderPath(object window)
    {
        object? document = null;
        object? folder = null;
        object? self = null;
        try
        {
            document = window.GetType().InvokeMember("Document", BindingFlags.GetProperty, null, window, null);
            if (document is null)
            {
                return null;
            }

            folder = document.GetType().InvokeMember("Folder", BindingFlags.GetProperty, null, document, null);
            if (folder is null)
            {
                return null;
            }

            self = folder.GetType().InvokeMember("Self", BindingFlags.GetProperty, null, folder, null);
            return self?.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, self, null) as string;
        }
        finally
        {
            Release(self);
            Release(folder);
            Release(document);
        }
    }

    private static string ClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(hwnd, buffer, buffer.Capacity) == 0 ? string.Empty : buffer.ToString();
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);
}
