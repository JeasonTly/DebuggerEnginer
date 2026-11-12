using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace TestEngineer.Desktop.Services;

/// <summary>截屏/录屏完成后的提示。可直接打开文件所在目录。</summary>
internal static class SavedFileNotice
{
    public static void Show(string kind, string path, bool failed = false, string? detail = null)
    {
        var exists = !failed && File.Exists(path) && new FileInfo(path).Length > 0;
        var win = new Window
        {
            Title = kind,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)Application.Current!.FindResource("Brush.Surface"),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI"),
            FontSize = 14,
            ShowInTaskbar = false,
        };

        var message = exists
            ? "已保存：\n" + path
            : (string.IsNullOrWhiteSpace(detail)
                ? "没有生成可播放的文件。\n" + path
                : detail + "\n" + path);

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 8),
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Brush.Text"),
        };

        var open = new Button
        {
            Content = "打开所在位置",
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 112,
            Style = (Style)Application.Current.FindResource("Button.Secondary"),
            IsEnabled = File.Exists(path),
        };
        open.Click += (_, _) => OpenContainingFolder(path);

        var close = new Button
        {
            Content = "关闭",
            MinWidth = 72,
            IsDefault = true,
            IsCancel = true,
            Style = (Style)Application.Current.FindResource("Button.Primary"),
        };
        close.Click += (_, _) => win.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 8, 16, 16),
        };
        buttons.Children.Add(open);
        buttons.Children.Add(close);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(text);
        win.Content = root;
        ModalSession.Show(win);
    }

    private static void OpenContainingFolder(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "/select,\"" + path + "\"",
            UseShellExecute = true,
        });
    }
}
