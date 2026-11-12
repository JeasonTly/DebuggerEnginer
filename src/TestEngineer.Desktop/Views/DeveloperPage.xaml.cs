using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TestEngineer.Desktop.ViewModels;

namespace TestEngineer.Desktop.Views;

public partial class DeveloperPage : UserControl
{
    public DeveloperPage()
    {
        InitializeComponent();
    }

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPresetDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not DeveloperPageViewModel vm)
        {
            return;
        }

        var apk = FirstApk(ReadFiles(e));
        if (apk is null)
        {
            vm.PresetStatus = "请拖入 APK 文件";
            e.Handled = true;
            return;
        }

        vm.ApplyPresetApkPath(apk);
        e.Handled = true;
    }

    private async void OnAnalyzeDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not DeveloperPageViewModel vm)
        {
            return;
        }

        var apk = FirstApk(ReadFiles(e));
        if (apk is null)
        {
            vm.AnalyzeStatus = "请拖入 APK 文件";
            e.Handled = true;
            return;
        }

        await vm.ApplyAnalyzeApkAsync(apk);
        e.Handled = true;
    }

    private void OnSignDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not DeveloperPageViewModel vm)
        {
            return;
        }

        var files = ReadFiles(e);
        if (files.Length == 0)
        {
            e.Handled = true;
            return;
        }

        var box = FindTextBox(e.OriginalSource as DependencyObject);
        if (box == SignApkBox)
        {
            var apk = FirstApk(files);
            if (apk is null)
            {
                vm.SignStatus = "请把 APK 拖到这里";
            }
            else
            {
                vm.ApplySignApkPath(apk);
            }
        }
        else if (box == SignKeystoreBox)
        {
            var keystore = FirstKeystore(files) ?? FirstNonApk(files);
            if (keystore is null)
            {
                vm.SignStatus = "请把签名文件拖到这里";
            }
            else
            {
                vm.ApplySignKeystorePath(keystore);
            }
        }
        else
        {
            var apk = FirstApk(files);
            var keystore = FirstKeystore(files);
            if (apk is not null)
            {
                vm.ApplySignApkPath(apk);
            }

            if (keystore is not null)
            {
                vm.ApplySignKeystorePath(keystore);
            }

            if (apk is null && keystore is null)
            {
                vm.SignStatus = "请拖入 APK 或签名文件";
            }
        }

        e.Handled = true;
    }

    private static string[] ReadFiles(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return [];
        }

        return e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
    }

    private static string? FirstApk(string[] files) =>
        files.FirstOrDefault(path => path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) && File.Exists(path));

    private static string? FirstKeystore(string[] files) =>
        files.FirstOrDefault(path => IsKeystore(path) && File.Exists(path));

    private static string? FirstNonApk(string[] files) =>
        files.FirstOrDefault(path => File.Exists(path) && !path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));

    private static bool IsKeystore(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".jks", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".keystore", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".p12", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".pfx", StringComparison.OrdinalIgnoreCase);
    }

    private static TextBox? FindTextBox(DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (origin is TextBox box)
            {
                return box;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return null;
    }
}
