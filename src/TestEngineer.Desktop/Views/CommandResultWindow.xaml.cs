using System.Text;
using System.Windows;

namespace TestEngineer.Desktop.Views;

public partial class CommandResultWindow : Window
{
    private readonly string _rawOutput;

    public CommandResultWindow(string title, string output)
    {
        InitializeComponent();
        Title = title;
        _rawOutput = NormalizeOutput(output);
        OutputBlock.Text = _rawOutput;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_rawOutput);
        }
        catch
        {
            // 剪贴板被占用时忽略
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>统一换行、展开 Tab，保证等宽字体下逐列对齐。</summary>
    private static string NormalizeOutput(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return string.Empty;
        }

        var text = output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\t', ' ');

        var lines = text.Split('\n');
        var sb = new StringBuilder(text.Length + 16);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(Environment.NewLine);
            }

            sb.Append(lines[i].TrimEnd());
        }

        return sb.ToString();
    }
}
