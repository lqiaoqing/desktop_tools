using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace DesktopTools;

public sealed class HelperLaunchResult
{
    public required OperationResult Operation { get; init; }
    public int? ExitCode { get; init; }
}

public sealed class HelperLauncher
{
    public static readonly TimeSpan DefaultWaitBudget = TimeSpan.FromSeconds(30);

    private readonly Func<string, int, long, TimeSpan, HelperLaunchResult> _launchAndWait;
    private readonly Func<string> _helperPathProvider;

    public HelperLauncher()
        : this(DefaultLaunchAndWait, DefaultHelperPath)
    {
    }

    internal HelperLauncher(
        Func<string, int, long, TimeSpan, HelperLaunchResult> launchAndWait,
        Func<string> helperPathProvider)
    {
        _launchAndWait = launchAndWait;
        _helperPathProvider = helperPathProvider;
    }

    public HelperLaunchResult RunCacheFlush(TimeSpan? waitBudget = null)
    {
        TimeSpan budget = waitBudget ?? DefaultWaitBudget;
        string path;
        try
        {
            path = _helperPathProvider();
        }
        catch (Exception ex)
        {
            return new HelperLaunchResult
            {
                Operation = OperationResult.Failed($"Helper path resolution failed: {ex.Message}")
            };
        }

        if (!File.Exists(path))
        {
            return new HelperLaunchResult
            {
                Operation = OperationResult.Failed($"Helper not found: {path}")
            };
        }

        var current = Process.GetCurrentProcess();
        long startUtcFileTime;
        try
        {
            startUtcFileTime = current.StartTime.ToFileTimeUtc();
        }
        catch (Exception ex)
        {
            return new HelperLaunchResult
            {
                Operation = OperationResult.Failed($"Cannot read parent start time: {ex.Message}")
            };
        }

        try
        {
            return _launchAndWait(path, current.Id, startUtcFileTime, budget);
        }
        catch (Win32Exception ex)
        {
            return new HelperLaunchResult
            {
                Operation = OperationResult.Failed(
                    ex.NativeErrorCode == 1223
                        ? "UserCanceled"
                        : $"Helper elevation launch failed, Win32Error={ex.NativeErrorCode}: {ex.Message}")
            };
        }
        catch (Exception ex)
        {
            return new HelperLaunchResult
            {
                Operation = OperationResult.Failed($"Helper launch failed: {ex.Message}")
            };
        }
    }

    private static string DefaultHelperPath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "helper", "DesktopTools.Helper.exe"));

    private static HelperLaunchResult DefaultLaunchAndWait(
        string path,
        int parentPid,
        long parentStartUtcFileTime,
        TimeSpan budget)
    {
        var psi = new ProcessStartInfo
        {
            FileName = path,
            Arguments = $"cache-flush --parent-pid {parentPid} --parent-start {parentStartUtcFileTime}",
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new HelperLaunchResult
            {
                Operation = OperationResult.Failed("Helper process did not start")
            };
        }

        if (!process.WaitForExit((int)budget.TotalMilliseconds))
        {
            // 主程序不强制结束高权限 Helper；按结果未知处理。
            return new HelperLaunchResult
            {
                Operation = OperationResult.Failed("ResultUnavailable: helper wait timed out")
            };
        }

        int exitCode = process.ExitCode;
        OperationResult operation = exitCode switch
        {
            0 => OperationResult.Success(),
            10 => OperationResult.Failed("Helper protocol/argument error (10)"),
            11 => OperationResult.Failed("Helper parent identity verification failed (11)"),
            12 => OperationResult.Failed("Helper privilege preparation failed (12)"),
            13 => OperationResult.Failed("System File Cache API failed (13)"),
            14 => OperationResult.Failed("Helper internal exception (14)"),
            15 => OperationResult.Failed("ResultUnavailable: helper watchdog self-exit (15)"),
            _ => OperationResult.Failed($"Helper abnormal exit code {exitCode}")
        };

        return new HelperLaunchResult { Operation = operation, ExitCode = exitCode };
    }
}
