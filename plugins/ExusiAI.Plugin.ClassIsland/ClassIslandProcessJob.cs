using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ExusiAI.Plugin.ClassIsland;

/// <summary>
/// Starts the upstream ClassIsland launcher suspended, places it in a kill-on-close Windows Job,
/// then resumes it. The ClassIsland.Desktop child inherits the same Job, so disabling ExusiAI's
/// plugin cannot leave an orphaned ClassIsland instance behind.
/// </summary>
internal sealed class ClassIslandProcessJob : IDisposable
{
    private const uint CreateSuspended = 0x00000004;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private IntPtr handle;
    private bool disposed;

    public ClassIslandProcessJob()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("ClassIsland 内置运行时仅支持 Windows。");

        handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建 ClassIsland 子进程作业。");

        var information = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };
        if (!SetInformationJobObject(
                handle,
                JobObjectInfoClass.ExtendedLimitInformation,
                ref information,
                (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "无法配置 ClassIsland 子进程作业。");
        }
    }

    public void StartLauncher(string launcherPath, string workingDirectory)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var startupInfo = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };
        var commandLine = new StringBuilder().Append('"').Append(launcherPath).Append('"');
        if (!CreateProcessW(
                launcherPath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CreateSuspended,
                IntPtr.Zero,
                workingDirectory,
                ref startupInfo,
                out var processInformation))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法启动内置 ClassIsland。");

        try
        {
            if (!AssignProcessToJobObject(handle, processInformation.Process))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法将 ClassIsland 绑定到 ExusiAI 生命周期。");

            if (ResumeThread(processInformation.Thread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法恢复 ClassIsland 启动器线程。");
        }
        catch
        {
            TerminateProcess(processInformation.Process, 1);
            throw;
        }
        finally
        {
            CloseHandle(processInformation.Thread);
            CloseHandle(processInformation.Process);
        }
    }

    public bool HasActiveProcesses()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var information = new JobObjectBasicAccountingInformation();
        if (!QueryInformationJobObject(
                handle,
                JobObjectInfoClass.BasicAccountingInformation,
                ref information,
                (uint)Marshal.SizeOf<JobObjectBasicAccountingInformation>(),
                IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 ClassIsland 进程状态。");
        return information.ActiveProcesses > 0;
    }

    public void RequestGracefulClose()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        foreach (var process in Process.GetProcessesByName("ClassIsland.Desktop"))
        {
            try
            {
                if (IsProcessInJob(process.Handle, handle, out var belongsToJob) && belongsToJob)
                    _ = process.CloseMainWindow();
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // The process may have exited between enumeration and inspection.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (handle != IntPtr.Zero)
        {
            CloseHandle(handle);
            handle = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }

    private enum JobObjectInfoClass
    {
        BasicAccountingInformation = 1,
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicAccountingInformation
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Count;
        public IntPtr Reserved2;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr job,
        JobObjectInfoClass infoClass,
        ref JobObjectExtendedLimitInformation information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        IntPtr job,
        JobObjectInfoClass infoClass,
        ref JobObjectBasicAccountingInformation information,
        uint informationLength,
        IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
