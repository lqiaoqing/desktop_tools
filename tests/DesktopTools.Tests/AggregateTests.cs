using DesktopTools;
using Xunit;

namespace DesktopTools.Tests;

public class AggregateTests
{
    [Fact]
    public void Both_Success_Returns_Success()
    {
        var overall = MemoryCleanupService.Aggregate(
            OperationResult.Success(),
            OperationResult.Success());
        Assert.Equal(CleanupOverallStatus.Success, overall);
    }

    [Fact]
    public void WorkingSet_Success_Cache_Skipped_Returns_PartialSuccess()
    {
        var overall = MemoryCleanupService.Aggregate(
            OperationResult.Success(),
            OperationResult.Skipped("UserCanceled"));
        Assert.Equal(CleanupOverallStatus.PartialSuccess, overall);
    }

    [Fact]
    public void WorkingSet_Failed_Cache_Success_Returns_PartialSuccess()
    {
        var overall = MemoryCleanupService.Aggregate(
            OperationResult.Failed("trim failed"),
            OperationResult.Success());
        Assert.Equal(CleanupOverallStatus.PartialSuccess, overall);
    }

    [Fact]
    public void Both_Failed_Returns_Failed()
    {
        var overall = MemoryCleanupService.Aggregate(
            OperationResult.Failed("a"),
            OperationResult.Failed("b"));
        Assert.Equal(CleanupOverallStatus.Failed, overall);
    }

    [Fact]
    public void Both_Skipped_Returns_Failed()
    {
        var overall = MemoryCleanupService.Aggregate(
            OperationResult.Skipped("BeforeQueryFailed"),
            OperationResult.Skipped("BeforeQueryFailed"));
        Assert.Equal(CleanupOverallStatus.Failed, overall);
    }

    [Fact]
    public void WorkingSet_Success_Cache_Failed_Returns_PartialSuccess()
    {
        var overall = MemoryCleanupService.Aggregate(
            OperationResult.Success(),
            OperationResult.Failed("helper failed"));
        Assert.Equal(CleanupOverallStatus.PartialSuccess, overall);
    }

    [Fact]
    public void WorkingSet_Failed_Cache_Skipped_Returns_Failed()
    {
        var overall = MemoryCleanupService.Aggregate(
            OperationResult.Failed("trim"),
            OperationResult.Skipped("UserCanceled"));
        Assert.Equal(CleanupOverallStatus.Failed, overall);
    }
}
