using DesktopTools;
using Xunit;

namespace DesktopTools.Tests;

public class ModelTests
{
    [Fact]
    public void OperationResult_Factories_SetCorrectStatus()
    {
        var s = OperationResult.Success();
        Assert.Equal(OperationStatus.Success, s.Status);
        Assert.Null(s.Error);

        var f = OperationResult.Failed("err");
        Assert.Equal(OperationStatus.Failed, f.Status);
        Assert.Equal("err", f.Error);

        var k = OperationResult.Skipped("reason");
        Assert.Equal(OperationStatus.Skipped, k.Status);
        Assert.Equal("reason", k.Error);
    }

    [Fact]
    public void MemorySnapshot_IsAvailable_ReflectsQueryError()
    {
        var ok = new MemorySnapshot { Timestamp = DateTime.Now, TotalPhysicalMemory = 1, AvailablePhysicalMemory = 1 };
        Assert.True(ok.IsAvailable);

        var bad = new MemorySnapshot { Timestamp = DateTime.Now, QueryError = "x" };
        Assert.False(bad.IsAvailable);
    }

    [Fact]
    public void WindowsMemory_QueryMemory_ReturnsRealSystemData()
    {
        var snap = WindowsMemory.QueryMemory();
        Assert.NotNull(snap);
        Assert.True(snap.IsAvailable, snap.QueryError);
        Assert.True(snap.TotalPhysicalMemory > 0);
        Assert.True(snap.AvailablePhysicalMemory > 0);
        Assert.True(snap.MemoryLoad <= 100);
    }

    [Fact]
    public void WindowsMemory_EmptyCurrentProcessWorkingSet_Succeeds()
    {
        // 当前测试进程有权限对自身 EmptyWorkingSet。
        var result = WindowsMemory.EmptyCurrentProcessWorkingSet();
        Assert.Equal(OperationStatus.Success, result.Status);
    }
}
