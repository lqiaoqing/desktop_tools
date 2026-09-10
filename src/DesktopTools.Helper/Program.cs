using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopTools.Helper;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitProtocolError = 10;
    private const int ExitParentVerificationFailed = 11;
    private const int ExitPrivilegeFailed = 12;
    private const int ExitCacheApiFailed = 13;
    private const int ExitInternalException = 14;
    private const int ExitWatchdogTimeout = 15;

    private static int Main(string[] args)
    {
        IntPtr parentHandle = IntPtr.Zero;
        try
        {
            var parsed = ParseArgs(args);
            if (parsed is null)
            {
                SimpleLog.Error("Protocol error: unsupported arguments");
                return ExitProtocolError;
            }

            parentHandle = OpenAndVerifyParent(parsed.Value.ParentPid, parsed.Value.ParentStartUtcFileTime);
            if (parentHandle == IntPtr.Zero)
            {
                SimpleLog.Error("Parent process identity verification failed");
                return ExitParentVerificationFailed;
            }

            // 看门狗同时负责：30 秒预算、父进程持续监测。
            using var watchdog = new Watchdog(TimeSpan.FromSeconds(30), parentHandle);

            if (!EnableSeIncreaseQuotaPrivilege())
            {
                SimpleLog.Error("Privilege preparation failed");
                return ExitPrivilegeFailed;
            }

            if (!TryGetSystemFileCacheSize(out nuint beforeMin, out nuint beforeMax, out uint beforeFlags, out int beforeErr))
            {
                SimpleLog.Error($"GetSystemFileCacheSize(before) failed, Win32Error={beforeErr}");
                return ExitCacheApiFailed;
            }

            SimpleLog.Info($"Cache size before: min={beforeMin} max={beforeMax} flags={beforeFlags}");

            if (!SetSystemFileCacheSize(unchecked((nuint)(-1)), unchecked((nuint)(-1)), 0))
            {
                int error = Marshal.GetLastWin32Error();
                SimpleLog.Error($"SetSystemFileCacheSize failed, Win32Error={error}");
                return ExitCacheApiFailed;
            }

            if (!TryGetSystemFileCacheSize(out nuint afterMin, out nuint afterMax, out uint afterFlags, out int afterErr))
            {
                SimpleLog.Error($"GetSystemFileCacheSize(after) failed, Win32Error={afterErr}");
                // API 已成功；诊断失败只记录，不伪造清理结果。
            }
            else
            {
                SimpleLog.Info($"Cache size after: min={afterMin} max={afterMax} flags={afterFlags}");
                if (beforeMin != afterMin || beforeMax != afterMax || beforeFlags != afterFlags)
                {
                    SimpleLog.Error(
                        "System File Cache policy changed by this operation; acceptance requires unchanged limits/flags");
                }
            }

            SimpleLog.Info("SetSystemFileCacheSize succeeded");
            return ExitSuccess;
        }
        catch (Exception ex)
        {
            SimpleLog.Error($"Helper internal exception: {ex}");
            return ExitInternalException;
        }
        finally
        {
            if (parentHandle != IntPtr.Zero)
            {
                CloseHandle(parentHandle);
            }
        }
    }

    private readonly record struct HelperArgs(uint ParentPid, long ParentStartUtcFileTime);

    private static HelperArgs? ParseArgs(string[] args)
    {
        if (args.Length == 0 || args[0] != "cache-flush")
        {
            return null;
        }

        uint pid = 0;
        long start = 0;
        bool hasPid = false;
        bool hasStart = false;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--parent-pid" && i + 1 < args.Length)
            {
                if (!uint.TryParse(args[++i], out pid))
                {
                    return null;
                }

                hasPid = true;
            }
            else if (args[i] == "--parent-start" && i + 1 < args.Length)
            {
                if (!long.TryParse(args[++i], out start))
                {
                    return null;
                }

                hasStart = true;
            }
            else
            {
                return null;
            }
        }

        return hasPid && hasStart ? new HelperArgs(pid, start) : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        IntPtr hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const uint WaitObject0 = 0;

    /// <summary>打开并校验父进程；成功返回仍持有的句柄（供持续监测），失败返回 Zero。</summary>
    private static IntPtr OpenAndVerifyParent(uint pid, long expectedStartUtcFileTime)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation | Synchronize, false, pid);
        if (handle == IntPtr.Zero)
        {
            SimpleLog.Error($"OpenProcess failed, Win32Error={Marshal.GetLastWin32Error()}");
            return IntPtr.Zero;
        }

        if (!GetProcessTimes(handle, out long creationTime, out _, out _, out _))
        {
            SimpleLog.Error($"GetProcessTimes failed, Win32Error={Marshal.GetLastWin32Error()}");
            CloseHandle(handle);
            return IntPtr.Zero;
        }

        if (creationTime != expectedStartUtcFileTime)
        {
            SimpleLog.Error("Parent creation time mismatch");
            CloseHandle(handle);
            return IntPtr.Zero;
        }

        uint wait = WaitForSingleObject(handle, 0);
        if (wait == WaitObject0)
        {
            SimpleLog.Error("Parent process already exited");
            CloseHandle(handle);
            return IntPtr.Zero;
        }

        return handle;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out long lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        bool DisableAllPrivileges,
        ref TokenPrivileges NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privileges;
    }

    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;
    private const int ErrorNotAllAssigned = 1300;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private static bool EnableSeIncreaseQuotaPrivilege()
    {
        IntPtr process = GetCurrentProcess();
        if (!OpenProcessToken(process, TokenAdjustPrivileges | TokenQuery, out IntPtr token))
        {
            SimpleLog.Error($"OpenProcessToken failed, Win32Error={Marshal.GetLastWin32Error()}");
            return false;
        }

        try
        {
            if (!LookupPrivilegeValue(null, "SeIncreaseQuotaPrivilege", out long luidRaw))
            {
                SimpleLog.Error($"LookupPrivilegeValue failed, Win32Error={Marshal.GetLastWin32Error()}");
                return false;
            }

            var luid = new Luid
            {
                LowPart = (uint)(luidRaw & 0xFFFFFFFF),
                HighPart = (int)(luidRaw >> 32)
            };

            var tp = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Privileges = new LuidAndAttributes
                {
                    Luid = luid,
                    Attributes = SePrivilegeEnabled
                }
            };

            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
            {
                SimpleLog.Error($"AdjustTokenPrivileges failed, Win32Error={Marshal.GetLastWin32Error()}");
                return false;
            }

            int lastError = Marshal.GetLastWin32Error();
            if (lastError == ErrorNotAllAssigned)
            {
                SimpleLog.Error("AdjustTokenPrivileges: ERROR_NOT_ALL_ASSIGNED");
                return false;
            }

            return true;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemFileCacheSize(nuint MinimumFileCacheSize, nuint MaximumFileCacheSize, uint Flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemFileCacheSize(out nuint MinimumFileCacheSize, out nuint MaximumFileCacheSize, out uint Flags);

    private static bool TryGetSystemFileCacheSize(out nuint min, out nuint max, out uint flags, out int win32Error)
    {
        if (!GetSystemFileCacheSize(out min, out max, out flags))
        {
            win32Error = Marshal.GetLastWin32Error();
            return false;
        }

        win32Error = 0;
        return true;
    }

    private sealed class Watchdog : IDisposable
    {
        private readonly System.Threading.Timer _budgetTimer;
        private readonly System.Threading.Timer _parentTimer;
        private readonly IntPtr _parentHandle;
        private int _fired;

        public Watchdog(TimeSpan budget, IntPtr parentHandle)
        {
            _parentHandle = parentHandle;

            _budgetTimer = new System.Threading.Timer(_ => Fire(ExitWatchdogTimeout, "budget expired"),
                null, budget, Timeout.InfiniteTimeSpan);

            // 持续监测父进程；退出或监测失败立即自终止。
            _parentTimer = new System.Threading.Timer(_ =>
            {
                if (_parentHandle == IntPtr.Zero)
                {
                    return;
                }

                uint wait = WaitForSingleObject(_parentHandle, 0);
                if (wait == WaitObject0)
                {
                    Fire(ExitParentVerificationFailed, "parent exited");
                }
                else if (wait == 0xFFFFFFFF)
                {
                    Fire(ExitParentVerificationFailed, $"parent wait failed, Win32Error={Marshal.GetLastWin32Error()}");
                }
            }, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        }

        private void Fire(int code, string reason)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                SimpleLog.Error($"Watchdog self-exit ({code}): {reason}");
                Environment.Exit(code);
            }
        }

        public void Dispose()
        {
            _budgetTimer.Dispose();
            _parentTimer.Dispose();
        }
    }
}

internal static class SimpleLog
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTools", "logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "helper.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
