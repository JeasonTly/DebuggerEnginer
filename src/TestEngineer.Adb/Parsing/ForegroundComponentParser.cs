using System.Text.RegularExpressions;

namespace TestEngineer.Adb.Parsing;

/// <summary>从 dumpsys 里抽出前台 Activity，只保留包名和组件名。</summary>
public static partial class ForegroundComponentParser
{
    [GeneratedRegex(
        @"\b(?<pkg>[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+)/(?<cls>\.?[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ComponentRegex();

    public static string Format(string? output)
    {
        var bestScore = -1;
        string? package = null;
        string? component = null;
        foreach (var raw in (output ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var match = ComponentRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var score = Score(line);
            if (score < bestScore || (score == bestScore && package is not null))
            {
                continue;
            }

            bestScore = score;
            package = match.Groups["pkg"].Value;
            var cls = match.Groups["cls"].Value;
            component = cls.StartsWith('.') ? package + cls : cls;
        }

        return package is null || component is null
            ? "未找到前台 Activity"
            : $"包名  {package}{Environment.NewLine}组件  {component}";
    }

    private static int Score(string line)
    {
        if (line.Contains("mResumedActivity", StringComparison.Ordinal)
            || line.Contains("topResumedActivity", StringComparison.Ordinal))
        {
            return 3;
        }

        if (line.Contains("ResumedActivity", StringComparison.Ordinal))
        {
            return 2;
        }

        if (line.Contains("mFocusedApp", StringComparison.Ordinal)
            || line.Contains("mCurrentFocus", StringComparison.Ordinal))
        {
            return 1;
        }

        return 0;
    }
}
