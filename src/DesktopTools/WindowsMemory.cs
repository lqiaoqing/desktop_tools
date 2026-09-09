using System.Runtime.InteropServices;

namespace DesktopTools;

public static class WindowsMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hwSnapshot);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    public static MemorySnapshot QueryMemory()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            int error = Marshal.GetLastWin32Error();
            return new MemorySnapshot
            {
                Timestamp = DateTime.Now,
                QueryError = $"GlobalMemoryStatusEx failed, Win32Error={error}"
            };
        }

        return new MemorySnapshot
        {
            Timestamp = DateTime.Now,
            TotalPhysicalMemory = status.ullTotalPhys,
            AvailablePhysicalMemory = status.ullAvailPhys,
            MemoryLoad = status.dwMemoryLoad
        };
    }

    public static OperationResult EmptyCurrentProcessWorkingSet()
    {
        IntPtr handle = GetCurrentProcess();
        if (!EmptyWorkingSet(handle))
        {
            int error = Marshal.GetLastWin32Error();
            return OperationResult.Failed($"EmptyWorkingSet failed, Win32Error={error}");
        }

        return OperationResult.Success();
    }
}
