using System.IO;
using DesktopTools;
using Xunit;

namespace DesktopTools.Tests;

public class HelperLauncherTests
{
    [Fact]
    public void MissingHelperFile_ReturnsFailed()
    {
        var launcher = new HelperLauncher(
            (_, _, _, _) => throw new InvalidOperationException("should not launch"),
            () => Path.Combine(Path.GetTempPath(), "no-such-helper-dir", "DesktopTools.Helper.exe"));

        var result = launcher.RunCacheFlush();

        Assert.Equal(OperationStatus.Failed, result.Operation.Status);
        Assert.Contains("Helper not found", result.Operation.Error);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public void PathProviderThrows_ReturnsFailed()
    {
        var launcher = new HelperLauncher(
            (_, _, _, _) => throw new InvalidOperationException("should not launch"),
            () => throw new IOException("path fail"));

        var result = launcher.RunCacheFlush();

        Assert.Equal(OperationStatus.Failed, result.Operation.Status);
        Assert.Contains("Helper path resolution failed", result.Operation.Error);
    }

    [Fact]
    public void LaunchReturnsZero_ExitCodeZero()
    {
        var temp = Path.Combine(Path.GetTempPath(), "dt-helper-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string exe = Path.Combine(temp, "DesktopTools.Helper.exe");
        File.WriteAllText(exe, "stub");

        try
        {
            var launcher = new HelperLauncher(
                (path, pid, start, budget) =>
                {
                    Assert.Equal(exe, path);
                    Assert.True(pid > 0);
                    Assert.True(start != 0);
                    Assert.Equal(TimeSpan.FromSeconds(30), budget);
                    return new HelperLaunchResult
                    {
                        Operation = OperationResult.Success(),
                        ExitCode = 0
                    };
                },
                () => exe);

            var result = launcher.RunCacheFlush();

            Assert.Equal(OperationStatus.Success, result.Operation.Status);
            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    [Fact]
    public void LaunchReturnsExitCode13_MapsToFailed()
    {
        var temp = Path.Combine(Path.GetTempPath(), "dt-helper-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string exe = Path.Combine(temp, "DesktopTools.Helper.exe");
        File.WriteAllText(exe, "stub");

        try
        {
            var launcher = new HelperLauncher(
                (_, _, _, _) => new HelperLaunchResult
                {
                    Operation = OperationResult.Failed("System File Cache API failed (13)"),
                    ExitCode = 13
                },
                () => exe);

            var result = launcher.RunCacheFlush();

            Assert.Equal(OperationStatus.Failed, result.Operation.Status);
            Assert.Equal(13, result.ExitCode);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }
}
