using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace TestEngineer.Infrastructure.Tools;

public sealed class ApkSigningException : Exception
{
    public ApkSigningException(string message)
        : base(message)
    {
    }
}

public sealed record ApkSignRequest(
    string InputApk,
    string OutputApk,
    string KeystorePath,
    string Alias,
    string StorePassword,
    string? KeyPassword);

public static class ApkSigning
{
    public static async Task<IReadOnlyList<string>> ListAliasesAsync(
        string keystorePath,
        string storePassword,
        CancellationToken cancellationToken = default)
    {
        var keytool = FindKeytool();
        if (keytool is null)
        {
            throw new ApkSigningException("未找到 keytool，无法读取密钥别名。请填写别名，或安装 JDK。");
        }

        var start = new ProcessStartInfo(keytool)
        {
            StandardOutputEncoding = ToolEncoding(),
            StandardErrorEncoding = ToolEncoding(),
        };
        start.ArgumentList.Add("-list");
        start.ArgumentList.Add("-keystore");
        start.ArgumentList.Add(keystorePath);
        start.ArgumentList.Add("-storepass");
        start.ArgumentList.Add(storePassword);

        var (code, stdout, stderr) = await RunAsync(start, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        var output = Sanitize(stdout + "\n" + stderr, storePassword);
        if (code != 0)
        {
            throw new ApkSigningException("无法读取签名文件。\n" + Trim(output));
        }

        var aliases = new List<string>();
        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!raw.Contains("PrivateKeyEntry", StringComparison.Ordinal))
            {
                continue;
            }

            var comma = raw.IndexOfAny([',', '，']);
            if (comma <= 0)
            {
                continue;
            }

            var alias = raw[..comma].Trim();
            if (alias.Length > 0)
            {
                aliases.Add(alias);
            }
        }

        return aliases;
    }

    public static async Task SignAsync(string apksignerBat, ApkSignRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apksignerBat);
        ArgumentNullException.ThrowIfNull(request);
        var jar = Path.Combine(Path.GetDirectoryName(apksignerBat) ?? "", "lib", "apksigner.jar");
        if (!File.Exists(jar))
        {
            throw new ApkSigningException("apksigner 安装不完整，缺少 apksigner.jar。");
        }

        var java = FindJava();
        if (java is null)
        {
            throw new ApkSigningException("未找到 java，无法运行 apksigner。");
        }

        string? storeFile = null;
        string? keyFile = null;
        try
        {
            storeFile = WriteSecret(request.StorePassword);
            var start = new ProcessStartInfo(java)
            {
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            start.ArgumentList.Add("-Dfile.encoding=UTF-8");
            start.ArgumentList.Add("-jar");
            start.ArgumentList.Add(jar);
            start.ArgumentList.Add("sign");
            start.ArgumentList.Add("--ks");
            start.ArgumentList.Add(request.KeystorePath);
            start.ArgumentList.Add("--ks-key-alias");
            start.ArgumentList.Add(request.Alias);
            start.ArgumentList.Add("--ks-pass");
            start.ArgumentList.Add("file:" + storeFile);
            if (!string.IsNullOrEmpty(request.KeyPassword))
            {
                keyFile = WriteSecret(request.KeyPassword);
                start.ArgumentList.Add("--key-pass");
                start.ArgumentList.Add("file:" + keyFile);
            }

            start.ArgumentList.Add("--out");
            start.ArgumentList.Add(request.OutputApk);
            start.ArgumentList.Add(request.InputApk);

            var (code, stdout, stderr) = await RunAsync(start, TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            if (code != 0)
            {
                var output = Sanitize(stdout + "\n" + stderr, request.StorePassword, request.KeyPassword);
                throw new ApkSigningException(string.IsNullOrWhiteSpace(output) ? "签名失败。" : "签名失败。\n" + Trim(output));
            }
        }
        finally
        {
            DeleteQuiet(storeFile);
            DeleteQuiet(keyFile);
        }
    }

    private static async Task<(int Code, string Stdout, string Stderr)> RunAsync(
        ProcessStartInfo start,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new ApkSigningException("无法启动签名工具。");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, cancellationToken);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (!cancellationToken.IsCancellationRequested)
            {
                throw new ApkSigningException("签名超时。");
            }

            throw;
        }

        return (process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
    }

    private static string? FindJava()
    {
        var onPath = FindOnPath("java.exe");
        if (onPath is not null)
        {
            return onPath;
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (string.IsNullOrWhiteSpace(javaHome))
        {
            return null;
        }

        var candidate = Path.Combine(javaHome, "bin", "java.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? FindKeytool()
    {
        var onPath = FindOnPath("keytool.exe");
        if (onPath is not null)
        {
            return onPath;
        }

        var java = FindJava();
        if (java is null)
        {
            return null;
        }

        var candidate = Path.Combine(Path.GetDirectoryName(java) ?? "", "keytool.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }

    private static string WriteSecret(string secret)
    {
        var path = Path.Combine(Path.GetTempPath(), "te-sign-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(secret));
        return path;
    }

    private static void DeleteQuiet(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static Encoding ToolEncoding() =>
        OperatingSystem.IsWindows() ? Encoding.GetEncoding(0) : Encoding.UTF8;

    private static string Sanitize(string text, params string?[] secrets)
    {
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret))
            {
                text = text.Replace(secret, "******", StringComparison.Ordinal);
            }
        }

        return text;
    }

    private static string Trim(string text)
    {
        text = text.Trim();
        return text.Length <= 800 ? text : text[..800] + "…";
    }
}
