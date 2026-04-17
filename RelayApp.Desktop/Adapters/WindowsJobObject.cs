using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RelayApp.Core.Broker;

namespace RelayApp.Desktop.Adapters;

internal sealed class WindowsJobObject : IDisposable
{
    private readonly IntPtr _handle;
    private bool _disposed;

    private WindowsJobObject(IntPtr handle)
    {
        _handle = handle;
    }

    public static WindowsJobObject? TryAttach(Process process, RelayJobObjectOptions? options = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        options ??= new RelayJobObjectOptions();

        // Use an unnamed job so this process owns the only live handle; when it closes,
        // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE can reap the entire assigned tree.
        var handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed.");
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags =
                    JobObjectLimitFlags.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
                    JobObjectLimitFlags.JOB_OBJECT_LIMIT_JOB_TIME |
                    JobObjectLimitFlags.JOB_OBJECT_LIMIT_ACTIVE_PROCESS |
                    JobObjectLimitFlags.JOB_OBJECT_LIMIT_JOB_MEMORY,
                PerJobUserTimeLimit = options.UserCpuTimePerJob.Ticks,
                // The Windows launch path uses a cmd.exe wrapper plus the target child,
                // so a floor of 2 keeps the job from self-terminating on startup.
                ActiveProcessLimit = (uint)Math.Max(2, options.ActiveProcessLimit),
            },
            JobMemoryLimit = new((ulong)Math.Max(1L, options.JobMemoryLimitBytes)),
        };

        try
        {
            var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var pointer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, pointer, fDeleteOld: false);
                if (!NativeMethods.SetInformationJobObject(
                        handle,
                        JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                        pointer,
                        (uint)length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject failed.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }

            if (!NativeMethods.AssignProcessToJobObject(handle, process.Handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject failed.");
            }
        }
        catch
        {
            // Close the CreateJobObject handle on any configuration / attach failure so
            // the underlying kernel job object is released instead of leaking for the
            // lifetime of the process.
            NativeMethods.CloseHandle(handle);
            throw;
        }

        return new WindowsJobObject(handle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_handle);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(
            IntPtr hJob,
            JOBOBJECTINFOCLASS JobObjectInfoClass,
            IntPtr lpJobObjectInfo,
            uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}

internal enum JOBOBJECTINFOCLASS
{
    JobObjectExtendedLimitInformation = 9,
}

[Flags]
internal enum JobObjectLimitFlags : uint
{
    JOB_OBJECT_LIMIT_JOB_TIME = 0x00000004,
    JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008,
    JOB_OBJECT_LIMIT_JOB_MEMORY = 0x00000200,
    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000,
}

[StructLayout(LayoutKind.Sequential)]
internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
{
    public long PerProcessUserTimeLimit;
    public long PerJobUserTimeLimit;
    public JobObjectLimitFlags LimitFlags;
    public UIntPtr MinimumWorkingSetSize;
    public UIntPtr MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public UIntPtr Affinity;
    public uint PriorityClass;
    public uint SchedulingClass;
}

[StructLayout(LayoutKind.Sequential)]
internal struct IO_COUNTERS
{
    public ulong ReadOperationCount;
    public ulong WriteOperationCount;
    public ulong OtherOperationCount;
    public ulong ReadTransferCount;
    public ulong WriteTransferCount;
    public ulong OtherTransferCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
{
    public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
    public IO_COUNTERS IoInfo;
    public UIntPtr ProcessMemoryLimit;
    public UIntPtr JobMemoryLimit;
    public UIntPtr PeakProcessMemoryUsed;
    public UIntPtr PeakJobMemoryUsed;
}
