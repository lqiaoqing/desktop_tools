namespace DesktopTools;

public sealed class MemoryCleanupService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<MemorySnapshot> _queryMemory;
    private readonly Func<OperationResult> _emptyWorkingSet;
    private readonly Func<TimeSpan?, HelperLaunchResult> _runHelper;
    private readonly object _stateLock = new();
    private int _querySequence;
    private int _publishedSequence;
    private int _exitRequested;

    public MemorySnapshot? CurrentSnapshot { get; private set; }
    public bool IsCleaning { get; private set; }
    public CleanupResult? LastCleanupResult { get; private set; }
    public bool ExitRequested => Volatile.Read(ref _exitRequested) == 1;

    public event EventHandler? StateChanged;
    public event EventHandler<CleanupResult>? CleanupCompleted;

    public MemoryCleanupService()
        : this(WindowsMemory.QueryMemory, WindowsMemory.EmptyCurrentProcessWorkingSet, DefaultHelper)
    {
    }

    internal MemoryCleanupService(
        Func<MemorySnapshot> queryMemory,
        Func<OperationResult> emptyWorkingSet,
        Func<TimeSpan?, HelperLaunchResult> runHelper)
    {
        _queryMemory = queryMemory;
        _emptyWorkingSet = emptyWorkingSet;
        _runHelper = runHelper;
    }

    private static HelperLaunchResult DefaultHelper(TimeSpan? budget) =>
        new HelperLauncher().RunCacheFlush(budget);

    public MemorySnapshot? RefreshSnapshot()
    {
        int seq = Interlocked.Increment(ref _querySequence);
        MemorySnapshot snapshot;
        try
        {
            snapshot = _queryMemory();
        }
        catch (Exception ex)
        {
            snapshot = new MemorySnapshot
            {
                Timestamp = DateTime.Now,
                QueryError = $"Memory query exception: {ex.Message}"
            };
        }

        lock (_stateLock)
        {
            if (seq < _publishedSequence)
            {
                return CurrentSnapshot;
            }

            _publishedSequence = seq;
            CurrentSnapshot = snapshot;
        }

        RaiseStateChanged();
        return snapshot;
    }

    public void RequestExit()
    {
        Volatile.Write(ref _exitRequested, 1);
        RaiseStateChanged();
    }

    public bool TryBeginCleanup(out Task<CleanupResult>? cleanupTask)
    {
        cleanupTask = null;
        if (ExitRequested)
        {
            return false;
        }

        if (!_gate.Wait(0))
        {
            return false;
        }

        lock (_stateLock)
        {
            IsCleaning = true;
        }

        RaiseStateChanged();
        cleanupTask = Task.Run(RunCleanupCoreAsync);
        return true;
    }

    private async Task<CleanupResult> RunCleanupCoreAsync()
    {
        try
        {
            CleanupResult result = await Task.Run(ExecuteCleanup).ConfigureAwait(false);

            lock (_stateLock)
            {
                LastCleanupResult = result;
            }

            RaiseStateChanged();
            try
            {
                CleanupCompleted?.Invoke(this, result);
            }
            catch
            {
                // 通知失败不得影响结果发布或门闩释放。
            }

            return result;
        }
        finally
        {
            lock (_stateLock)
            {
                IsCleaning = false;
            }

            _gate.Release();
            RaiseStateChanged();
        }
    }

    private CleanupResult ExecuteCleanup()
    {
        Log.Info("Cleanup started");

        MemorySnapshot before;
        int beforeSeq = Interlocked.Increment(ref _querySequence);
        try
        {
            before = _queryMemory();
        }
        catch (Exception ex)
        {
            before = new MemorySnapshot
            {
                Timestamp = DateTime.Now,
                QueryError = $"Memory query exception: {ex.Message}"
            };
        }

        if (!string.IsNullOrEmpty(before.QueryError))
        {
            lock (_stateLock)
            {
                if (beforeSeq >= _publishedSequence)
                {
                    _publishedSequence = beforeSeq;
                    CurrentSnapshot = before;
                }
            }

            Log.Error($"Before memory query failed: {before.QueryError}");
            return new CleanupResult
            {
                OverallStatus = CleanupOverallStatus.Failed,
                BeforeSnapshot = null,
                AfterSnapshot = null,
                CurrentProcessWorkingSet = OperationResult.Skipped("BeforeQueryFailed"),
                SystemFileCache = OperationResult.Skipped("BeforeQueryFailed"),
                BeforeQueryError = before.QueryError,
                FailedStage = "BeforeQuery"
            };
        }

        lock (_stateLock)
        {
            if (beforeSeq >= _publishedSequence)
            {
                _publishedSequence = beforeSeq;
                CurrentSnapshot = before;
            }
        }

        RaiseStateChanged();

        OperationResult trimResult;
        try
        {
            trimResult = _emptyWorkingSet();
            if (trimResult.Status == OperationStatus.Success)
            {
                Log.Info("Current-process trim succeeded");
            }
            else
            {
                Log.Error($"Current-process trim failed: {trimResult.Error}");
            }
        }
        catch (Exception ex)
        {
            trimResult = OperationResult.Failed($"Current-process trim exception: {ex.Message}");
            Log.Error(trimResult.Error!);
        }

        OperationResult cacheResult;
        HelperLaunchResult helperLaunch;
        try
        {
            Log.Info("Elevation requested");
            helperLaunch = _runHelper(null);
            cacheResult = helperLaunch.Operation;
            if (cacheResult.Status == OperationStatus.Success)
            {
                Log.Info($"System File Cache cleanup succeeded (exit={helperLaunch.ExitCode})");
            }
            else if (cacheResult.Error == "UserCanceled")
            {
                Log.Info("Elevation canceled");
                cacheResult = OperationResult.Skipped("UserCanceled");
            }
            else
            {
                Log.Error($"System File Cache cleanup failed: {cacheResult.Error}");
            }
        }
        catch (Exception ex)
        {
            helperLaunch = new HelperLaunchResult { Operation = OperationResult.Failed(ex.Message) };
            cacheResult = OperationResult.Failed($"Helper launch exception: {ex.Message}");
            Log.Error(cacheResult.Error!);
        }

        MemorySnapshot? after = null;
        string? afterError = null;
        int afterSeq = Interlocked.Increment(ref _querySequence);
        try
        {
            after = _queryMemory();
        }
        catch (Exception ex)
        {
            after = new MemorySnapshot
            {
                Timestamp = DateTime.Now,
                QueryError = $"Memory query exception: {ex.Message}"
            };
        }

        if (!string.IsNullOrEmpty(after.QueryError))
        {
            afterError = after.QueryError;
            after = null;
            Log.Error($"After memory query failed: {afterError}");
        }
        else
        {
            lock (_stateLock)
            {
                if (afterSeq >= _publishedSequence)
                {
                    _publishedSequence = afterSeq;
                    CurrentSnapshot = after!;
                }
            }
        }

        CleanupOverallStatus overall = Aggregate(trimResult, cacheResult);
        Log.Info($"Cleanup completed: {overall}");

        return new CleanupResult
        {
            OverallStatus = overall,
            BeforeSnapshot = before,
            AfterSnapshot = after,
            CurrentProcessWorkingSet = trimResult,
            SystemFileCache = cacheResult,
            AfterQueryError = afterError,
            FailedStage = overall == CleanupOverallStatus.Success ? null : "Execution"
        };
    }

    internal static CleanupOverallStatus Aggregate(OperationResult workingSet, OperationResult cache)
    {
        bool anySuccess =
            workingSet.Status == OperationStatus.Success
            || cache.Status == OperationStatus.Success;
        bool allSuccess =
            workingSet.Status == OperationStatus.Success
            && cache.Status == OperationStatus.Success;

        if (allSuccess)
        {
            return CleanupOverallStatus.Success;
        }

        return anySuccess ? CleanupOverallStatus.PartialSuccess : CleanupOverallStatus.Failed;
    }

    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
        }
    }
}
