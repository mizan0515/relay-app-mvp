using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RelayApp.Desktop.Adapters;

internal sealed class NativeProcessLaunch : IDisposable
{
    private IntPtr _threadHandle;
    private bool _resumed;

    public NativeProcessLaunch(
        Process process,
        StreamReader standardOutput,
        StreamReader standardError,
        IntPtr threadHandle)
    {
        Process = process;
        StandardOutput = standardOutput;
        StandardError = standardError;
        _threadHandle = threadHandle;
    }

    public Process Process { get; }

    public StreamReader StandardOutput { get; }

    public StreamReader StandardError { get; }

    public void Resume()
    {
        if (_resumed || _threadHandle == IntPtr.Zero)
        {
            return;
        }

        var result = NativeProcessLauncher.NativeMethods.ResumeThread(_threadHandle);
        if (result == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread failed.");
        }

        _resumed = true;
        NativeProcessLauncher.NativeMethods.CloseHandle(_threadHandle);
        _threadHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
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
            NativeProcessLauncher.NativeMethods.CloseHandle(_threadHandle);
            _threadHandle = IntPtr.Zero;
        }
    }
}

internal static class NativeProcessLauncher
{
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint STARTF_USESTDHANDLES = 0x00000100;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;
    private const uint STD_INPUT_HANDLE = unchecked((uint)-10);
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint GENERIC_READ = 0x80000000;

    public static NativeProcessLaunch StartSuspended(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var commandLine = BuildWindowsCommandLine(fileName, arguments);
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
        IntPtr threadHandle = IntPtr.Zero;
        Process? process = null;

        try
        {
            CreatePipePair(security, out stdoutRead, out stdoutWrite);
            CreatePipePair(security, out stderrRead, out stderrWrite);
            stdinRead = OpenNullHandle();

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
            var stdoutReader = new StreamReader(new FileStream(stdoutRead!, FileAccess.Read, 4096, isAsync: false), Encoding.UTF8);
            stdoutRead = null;
            var stderrReader = new StreamReader(new FileStream(stderrRead!, FileAccess.Read, 4096, isAsync: false), Encoding.UTF8);
            stderrRead = null;

            return new NativeProcessLaunch(process, stdoutReader, stderrReader, threadHandle);
        }
        catch
        {
            stdoutRead?.Dispose();
            stdoutWrite?.Dispose();
            stderrRead?.Dispose();
            stderrWrite?.Dispose();
            stdinRead?.Dispose();
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
        out SafeFileHandle writeHandle)
    {
        if (!NativeMethods.CreatePipe(out readHandle, out writeHandle, ref securityAttributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed.");
        }

        if (!NativeMethods.SetHandleInformation(readHandle, HANDLE_FLAG_INHERIT, 0))
        {
            readHandle.Dispose();
            writeHandle.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation failed.");
        }
    }

    private static SafeFileHandle OpenNullHandle()
    {
        var handle = NativeMethods.CreateFileW(
            "NUL",
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFileW(NUL) failed.");
        }

        if (!NativeMethods.SetHandleInformation(handle, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT))
        {
            handle.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation(stdin) failed.");
        }

        return handle;
    }

    private static string BuildWindowsCommandLine(string fileName, IEnumerable<string> arguments)
    {
        var innerBuilder = new StringBuilder();
        innerBuilder.Append(QuoteArgument(fileName));
        foreach (var argument in arguments)
        {
            innerBuilder.Append(' ');
            innerBuilder.Append(QuoteArgument(argument));
        }

        if (!RequiresCmdShell(fileName))
        {
            return innerBuilder.ToString();
        }

        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(comSpec))
        {
            comSpec = "cmd.exe";
        }

        return $"{QuoteArgument(comSpec)} /d /s /c {QuoteArgument(innerBuilder.ToString())}";
    }

    private static bool RequiresCmdShell(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
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

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
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
