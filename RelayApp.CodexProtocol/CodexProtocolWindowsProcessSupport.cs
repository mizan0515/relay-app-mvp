using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using RelayApp.Core.Broker;

namespace RelayApp.CodexProtocol;

internal sealed class CodexProtocolProcessLaunch : IDisposable
{
    private IntPtr _threadHandle;
    private bool _resumed;

    public CodexProtocolProcessLaunch(
        Process process,
        StreamWriter standardInput,
        StreamReader standardOutput,
        StreamReader standardError,
        IntPtr threadHandle)
    {
        Process = process;
        StandardInput = standardInput;
        StandardOutput = standardOutput;
        StandardError = standardError;
        _threadHandle = threadHandle;
    }

    public Process Process { get; }

    public StreamWriter StandardInput { get; }

    public StreamReader StandardOutput { get; }

    public StreamReader StandardError { get; }

    public void Resume()
    {
        if (_resumed || _threadHandle == IntPtr.Zero)
        {
            return;
        }

        var result = CodexProtocolWindowsProcessLauncher.NativeMethods.ResumeThread(_threadHandle);
        if (result == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread failed.");
        }

        _resumed = true;
        CodexProtocolWindowsProcessLauncher.NativeMethods.CloseHandle(_threadHandle);
        _threadHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        try
        {
            StandardInput.Dispose();
        }
        catch
        {
        }

        try
        {
            StandardOutput.Dispose();
        }
        catch
        {
        }

        try
        {
            StandardError.Dispose();
        }
        catch
        {
        }

        if (_threadHandle != IntPtr.Zero)
        {
            CodexProtocolWindowsProcessLauncher.NativeMethods.CloseHandle(_threadHandle);
            _threadHandle = IntPtr.Zero;
        }
    }
}

internal static class CodexProtocolWindowsProcessLauncher
{
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint STARTF_USESTDHANDLES = 0x00000100;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;

    public static CodexProtocolProcessLaunch StartSuspended(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var commandLine = BuildWindowsShellCommandLine(fileName, arguments);
        var security = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
        };

        SafeFileHandle? stdoutRead = null;
        SafeFileHandle? stdoutWrite = null;
        SafeFileHandle? stderrRead = null;
        SafeFileHandle? stderrWrite = null;
        SafeFileHandle? stdinRead = null;
        SafeFileHandle? stdinWrite = null;
        IntPtr threadHandle = IntPtr.Zero;
        Process? process = null;

        try
        {
            CreatePipePair(security, out stdoutRead, out stdoutWrite, parentKeepsReadEnd: true);
            CreatePipePair(security, out stderrRead, out stderrWrite, parentKeepsReadEnd: true);
            CreatePipePair(security, out stdinRead, out stdinWrite, parentKeepsReadEnd: false);

            var startupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = STARTF_USESTDHANDLES,
                hStdInput = stdinRead.DangerousGetHandle(),
                hStdOutput = stdoutWrite.DangerousGetHandle(),
                hStdError = stderrWrite.DangerousGetHandle(),
            };

            if (!NativeMethods.CreateProcessW(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startupInfo,
                    out var processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateProcessW failed for '{fileName}'.");
            }

            threadHandle = processInfo.hThread;
            NativeMethods.CloseHandle(processInfo.hProcess);

            stdoutWrite.Dispose();
            stdoutWrite = null;
            stderrWrite.Dispose();
            stderrWrite = null;
            stdinRead.Dispose();
            stdinRead = null;

            process = Process.GetProcessById((int)processInfo.dwProcessId);
            var stdinWriter = new StreamWriter(new FileStream(stdinWrite!, FileAccess.Write, 4096, isAsync: false), new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            stdinWrite = null;
            var stdoutReader = new StreamReader(new FileStream(stdoutRead!, FileAccess.Read, 4096, isAsync: false), Encoding.UTF8);
            stdoutRead = null;
            var stderrReader = new StreamReader(new FileStream(stderrRead!, FileAccess.Read, 4096, isAsync: false), Encoding.UTF8);
            stderrRead = null;

            return new CodexProtocolProcessLaunch(process, stdinWriter, stdoutReader, stderrReader, threadHandle);
        }
        catch
        {
            stdoutRead?.Dispose();
            stdoutWrite?.Dispose();
            stderrRead?.Dispose();
            stderrWrite?.Dispose();
            stdinRead?.Dispose();
            stdinWrite?.Dispose();
            if (threadHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(threadHandle);
            }

            process?.Dispose();
            throw;
        }
    }

    private static void CreatePipePair(
        SECURITY_ATTRIBUTES securityAttributes,
        out SafeFileHandle readHandle,
        out SafeFileHandle writeHandle,
        bool parentKeepsReadEnd)
    {
        if (!NativeMethods.CreatePipe(out readHandle, out writeHandle, ref securityAttributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed.");
        }

        var parentHandle = parentKeepsReadEnd ? readHandle : writeHandle;
        if (!NativeMethods.SetHandleInformation(parentHandle, HANDLE_FLAG_INHERIT, 0))
        {
            readHandle.Dispose();
            writeHandle.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation failed.");
        }
    }

    private static string BuildWindowsShellCommandLine(string fileName, IEnumerable<string> arguments)
    {
        var innerBuilder = new StringBuilder();
        innerBuilder.Append(QuoteArgument(fileName));
        foreach (var argument in arguments)
        {
            innerBuilder.Append(' ');
            innerBuilder.Append(QuoteArgument(argument));
        }

        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(comSpec))
        {
            comSpec = "cmd.exe";
        }

        return $"{QuoteArgument(comSpec)} /d /s /c {QuoteArgument(innerBuilder.ToString())}";
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        var needsQuotes = argument.Any(ch => char.IsWhiteSpace(ch) || ch == '"');
        if (!needsQuotes)
        {
            return argument;
        }

        var builder = new StringBuilder();
        builder.Append('"');
        var backslashes = 0;
        foreach (var ch in argument)
        {
            if (ch == '\\')
            {
                backslashes++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                builder.Append('\\', backslashes);
                backslashes = 0;
            }

            builder.Append(ch);
        }

        if (backslashes > 0)
        {
            builder.Append('\\', backslashes * 2);
        }

        builder.Append('"');
        return builder.ToString();
    }

    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcessW(
            string? lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            [In] ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreatePipe(
            out SafeFileHandle hReadPipe,
            out SafeFileHandle hWritePipe,
            ref SECURITY_ATTRIBUTES lpPipeAttributes,
            int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetHandleInformation(
            SafeHandle hObject,
            uint dwMask,
            uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}

internal sealed class CodexProtocolWindowsJobObject : IDisposable
{
    private readonly IntPtr _handle;
    private bool _disposed;

    private CodexProtocolWindowsJobObject(IntPtr handle)
    {
        _handle = handle;
    }

    public static CodexProtocolWindowsJobObject? TryAttach(Process process, RelayJobObjectOptions? options = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        options ??= new RelayJobObjectOptions();

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
                // The Windows launch path uses a cmd.exe wrapper plus the codex child,
                // so a floor of 2 keeps the job from self-terminating on startup.
                ActiveProcessLimit = (uint)Math.Max(2, options.ActiveProcessLimit),
            },
            JobMemoryLimit = new((ulong)Math.Max(1L, options.JobMemoryLimitBytes)),
        };

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
            var error = Marshal.GetLastWin32Error();
            NativeMethods.CloseHandle(handle);
            throw new Win32Exception(error, "AssignProcessToJobObject failed.");
        }

        return new CodexProtocolWindowsJobObject(handle);
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

[StructLayout(LayoutKind.Sequential)]
internal struct SECURITY_ATTRIBUTES
{
    public int nLength;
    public IntPtr lpSecurityDescriptor;
    [MarshalAs(UnmanagedType.Bool)]
    public bool bInheritHandle;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct STARTUPINFO
{
    public int cb;
    public string? lpReserved;
    public string? lpDesktop;
    public string? lpTitle;
    public int dwX;
    public int dwY;
    public int dwXSize;
    public int dwYSize;
    public int dwXCountChars;
    public int dwYCountChars;
    public int dwFillAttribute;
    public uint dwFlags;
    public short wShowWindow;
    public short cbReserved2;
    public IntPtr lpReserved2;
    public IntPtr hStdInput;
    public IntPtr hStdOutput;
    public IntPtr hStdError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_INFORMATION
{
    public IntPtr hProcess;
    public IntPtr hThread;
    public int dwProcessId;
    public int dwThreadId;
}
