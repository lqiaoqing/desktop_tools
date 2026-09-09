using DesktopTools;
using Xunit;

namespace DesktopTools.Tests;

public class MemoryCleanupServiceTests
{
    private static MemorySnapshot OkSnapshot(ulong available = 4UL * 1024 * 1024 * 1024) =>
        new()
        {
            Timestamp = DateTime.Now,
            TotalPhysicalMemory = 16UL * 1024 * 1024 * 1024,
            AvailablePhysicalMemory = available,
            MemoryLoad = 50
        };

    private static MemorySnapshot FailSnapshot(string error) =>
        new() { Timestamp = DateTime.Now, QueryError = error };

    private static MemoryCleanupService CreateService(
        Func<MemorySnapshot> query,
        Func<OperationResult>? trim = null,
        Func<TimeSpan?, HelperLaunchResult>? helper = null)
    {
        return new MemoryCleanupService(
            query,
            trim ?? (() => OperationResult.Success()),
            helper ?? (_ => new HelperLaunchResult { Operation = OperationResult.Success(), ExitCode = 0 }));
    }

    [Fact]
    public async Task HappyPath_BothSucceed_ReturnsSuccess()
    {
        int queryCount = 0;
        var service = CreateService(
            () =>
            {
                queryCount++;
                return OkSnapshot(available: 4UL * 1024 * 1024 * 1024 + (ulong)queryCount);
            },
            () => OperationResult.Success(),
            _ => new HelperLaunchResult { Operation = OperationResult.Success(), ExitCode = 0 });

        Assert.True(service.TryBeginCleanup(out var task));
        Assert.NotNull(task);
        var result = await task!;

        Assert.Equal(CleanupOverallStatus.Success, result.OverallStatus);
        Assert.Equal(OperationStatus.Success, result.CurrentProcessWorkingSet.Status);
        Assert.Equal(OperationStatus.Success, result.SystemFileCache.Status);
        Assert.NotNull(result.BeforeSnapshot);
        Assert.NotNull(result.AfterSnapshot);
        Assert.False(service.IsCleaning);
    }

    [Fact]
    public async Task BeforeQueryFails_SkipsOperations_ReturnsFailed()
    {
        var service = CreateService(
            () => FailSnapshot("query failed"),
            () => throw new InvalidOperationException("should not run"),
            _ => throw new InvalidOperationException("should not run"));

        Assert.True(service.TryBeginCleanup(out var task));
        var result = await task!;

        Assert.Equal(CleanupOverallStatus.Failed, result.OverallStatus);
        Assert.Equal(OperationStatus.Skipped, result.CurrentProcessWorkingSet.Status);
        Assert.Equal(OperationStatus.Skipped, result.SystemFileCache.Status);
        Assert.Equal("BeforeQueryFailed", result.CurrentProcessWorkingSet.Error);
        Assert.Equal("BeforeQueryFailed", result.SystemFileCache.Error);
        Assert.NotNull(result.BeforeQueryError);
        Assert.False(service.IsCleaning);
    }

    [Fact]
    public async Task AfterQueryFails_KeepsOperationResults()
    {
        int call = 0;
        var service = CreateService(
            () =>
            {
                call++;
                return call == 1 ? OkSnapshot() : FailSnapshot("after fail");
            },
            () => OperationResult.Success(),
            _ => new HelperLaunchResult { Operation = OperationResult.Success(), ExitCode = 0 });

        Assert.True(service.TryBeginCleanup(out var task));
        var result = await task!;

        Assert.Equal(CleanupOverallStatus.Success, result.OverallStatus);
        Assert.Equal(OperationStatus.Success, result.CurrentProcessWorkingSet.Status);
        Assert.Equal(OperationStatus.Success, result.SystemFileCache.Status);
        Assert.NotNull(result.BeforeSnapshot);
        Assert.Null(result.AfterSnapshot);
        Assert.Equal("after fail", result.AfterQueryError);
        Assert.False(service.IsCleaning);
    }

    [Fact]
    public async Task UacCanceled_PartialSuccess()
    {
        var service = CreateService(
            () => OkSnapshot(),
            () => OperationResult.Success(),
            _ => new HelperLaunchResult { Operation = OperationResult.Skipped("UserCanceled") });

        Assert.True(service.TryBeginCleanup(out var task));
        var result = await task!;

        Assert.Equal(CleanupOverallStatus.PartialSuccess, result.OverallStatus);
        Assert.Equal(OperationStatus.Success, result.CurrentProcessWorkingSet.Status);
        Assert.Equal(OperationStatus.Skipped, result.SystemFileCache.Status);
        Assert.Equal("UserCanceled", result.SystemFileCache.Error);
    }

    [Fact]
    public async Task HelperLaunchFailed_OverallPartialOrFailed()
    {
        var service = CreateService(
            () => OkSnapshot(),
            () => OperationResult.Success(),
            _ => new HelperLaunchResult
            {
                Operation = OperationResult.Failed("Helper not found")
            });

        Assert.True(service.TryBeginCleanup(out var task));
        var result = await task!;

        Assert.Equal(CleanupOverallStatus.PartialSuccess, result.OverallStatus);
        Assert.Equal(OperationStatus.Failed, result.SystemFileCache.Status);
    }

    [Fact]
    public async Task HelperTimeout_ResultUnavailable()
    {
        var service = CreateService(
            () => OkSnapshot(),
            () => OperationResult.Success(),
            _ => new HelperLaunchResult
            {
                Operation = OperationResult.Failed("ResultUnavailable: helper wait timed out")
            });

        Assert.True(service.TryBeginCleanup(out var task));
        var result = await task!;

        Assert.Equal(CleanupOverallStatus.PartialSuccess, result.OverallStatus);
        Assert.Contains("ResultUnavailable", result.SystemFileCache.Error);
    }

    [Fact]
    public async Task ConcurrentCleanup_SecondRejected()
    {
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        int trimCalls = 0;

        var service = CreateService(
            () => OkSnapshot(),
            () =>
            {
                Interlocked.Increment(ref trimCalls);
                started.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return OperationResult.Success();
            });

        Assert.True(service.TryBeginCleanup(out var first));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        Assert.False(service.TryBeginCleanup(out var second));
        Assert.Null(second);

        release.Set();
        Assert.NotNull(first);
        await first!;
        Assert.Equal(1, trimCalls);
        Assert.False(service.IsCleaning);
    }

    [Fact]
    public async Task ExitRequested_RejectsNewCleanup()
    {
        var service = CreateService(() => OkSnapshot());
        service.RequestExit();

        Assert.False(service.TryBeginCleanup(out var task));
        Assert.Null(task);
        Assert.True(service.ExitRequested);
    }

    [Fact]
    public async Task OldQueryCannotOverwriteNewerSnapshot()
    {
        int seq = 0;
        var service = new MemoryCleanupService(
            () =>
            {
                int n = Interlocked.Increment(ref seq);
                return OkSnapshot(available: (ulong)n);
            },
            () => OperationResult.Success(),
            _ => new HelperLaunchResult { Operation = OperationResult.Success(), ExitCode = 0 });

        // 第一次刷新
        var s1 = service.RefreshSnapshot();
        Assert.NotNull(s1);
        ulong firstAvailable = s1!.AvailablePhysicalMemory;

        // 第二次刷新
        var s2 = service.RefreshSnapshot();
        Assert.NotNull(s2);
        Assert.True(s2!.AvailablePhysicalMemory >= firstAvailable);
    }

    [Fact]
    public async Task TrimFailure_CacheSuccess_PartialSuccess()
    {
        var service = CreateService(
            () => OkSnapshot(),
            () => OperationResult.Failed("EmptyWorkingSet failed, Win32Error=5"),
            _ => new HelperLaunchResult { Operation = OperationResult.Success(), ExitCode = 0 });

        Assert.True(service.TryBeginCleanup(out var task));
        var result = await task!;

        Assert.Equal(CleanupOverallStatus.PartialSuccess, result.OverallStatus);
        Assert.Equal(OperationStatus.Failed, result.CurrentProcessWorkingSet.Status);
        Assert.Contains("Win32Error=5", result.CurrentProcessWorkingSet.Error);
    }

    [Fact]
    public async Task BothFail_ReturnsFailed()
    {
        var service = CreateService(
            () => OkSnapshot(),
            () => OperationResult.Failed("trim fail"),
            _ => new HelperLaunchResult { Operation = OperationResult.Failed("cache fail") });

        Assert.True(service.TryBeginCleanup(out var task));
        var result = await task!;

        Assert.Equal(CleanupOverallStatus.Failed, result.OverallStatus);
        Assert.Equal(OperationStatus.Failed, result.CurrentProcessWorkingSet.Status);
        Assert.Equal(OperationStatus.Failed, result.SystemFileCache.Status);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotBlockCompletion()
    {
        var service = CreateService(() => OkSnapshot());
        bool completed = false;
        service.CleanupCompleted += (_, _) =>
        {
            completed = true;
            throw new InvalidOperationException("notify fail");
        };

        Assert.True(service.TryBeginCleanup(out var task));
        var result = await task!;

        Assert.Equal(CleanupOverallStatus.Success, result.OverallStatus);
        Assert.True(completed);
        Assert.False(service.IsCleaning);
    }

    [Fact]
    public void Refresh_UpdatesCurrentSnapshot()
    {
        var service = CreateService(() => OkSnapshot(available: 12345));
        var snap = service.RefreshSnapshot();

        Assert.NotNull(snap);
        Assert.True(snap!.IsAvailable);
        Assert.Equal(12345UL, snap.AvailablePhysicalMemory);
        Assert.NotNull(service.CurrentSnapshot);
        Assert.Equal(12345UL, service.CurrentSnapshot!.AvailablePhysicalMemory);
    }

    [Fact]
    public void Refresh_Failure_PublishesUnavailable()
    {
        var service = CreateService(() => FailSnapshot("boom"));
        var snap = service.RefreshSnapshot();

        Assert.NotNull(snap);
        Assert.False(snap!.IsAvailable);
        Assert.Equal("boom", snap.QueryError);
    }
}
