namespace DesktopTools;

public enum CleanupOverallStatus
{
    Success,
    PartialSuccess,
    Failed
}

public enum OperationStatus
{
    Success,
    Failed,
    Skipped
}

public sealed class MemorySnapshot
{
    public DateTime Timestamp { get; init; }
    public ulong TotalPhysicalMemory { get; init; }
    public ulong AvailablePhysicalMemory { get; init; }
    public uint MemoryLoad { get; init; }
    public string? QueryError { get; init; }
    public bool IsAvailable => QueryError is null;
}

public sealed class OperationResult
{
    public required OperationStatus Status { get; init; }
    public string? Error { get; init; }

    public static OperationResult Success() => new() { Status = OperationStatus.Success };

    public static OperationResult Failed(string error) =>
        new() { Status = OperationStatus.Failed, Error = error };

    public static OperationResult Skipped(string reason) =>
        new() { Status = OperationStatus.Skipped, Error = reason };
}

public sealed class CleanupResult
{
    public required CleanupOverallStatus OverallStatus { get; init; }
    public MemorySnapshot? BeforeSnapshot { get; init; }
    public MemorySnapshot? AfterSnapshot { get; init; }
    public required OperationResult CurrentProcessWorkingSet { get; init; }
    public required OperationResult SystemFileCache { get; init; }
    public string? BeforeQueryError { get; init; }
    public string? AfterQueryError { get; init; }
    public string? FailedStage { get; init; }
    public DateTime CompletedAt { get; init; } = DateTime.Now;
}
