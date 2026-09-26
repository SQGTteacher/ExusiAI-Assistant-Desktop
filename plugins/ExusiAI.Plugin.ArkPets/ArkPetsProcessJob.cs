using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ExusiAI.Plugin.ArkPets;

internal sealed class ArkPetsProcessJob : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private readonly SafeJobHandle handle;
    private bool disposed;

    public ArkPetsProcessJob()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("ArkPets process binding currently requires Windows.");

        handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建 ArkPets 子进程作业。");

        var information = new JobObjectExtendedLimitInformation();
        information.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
        if (!SetInformationJobObject(
                handle,
                JobObjectInfoClass.ExtendedLimitInformation,
                ref information,
                (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "无法配置 ArkPets 子进程作业。");
        }
    }

    public void Assign(Process process)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(process);

        if (!AssignProcessToJobObject(handle, process.Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法将 ArkPets 运行核心绑定到 ExusiAI。");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        handle.Dispose();
        GC.SuppressFinalize(this);
    }

    private enum JobObjectInfoClass
    {
        ExtendedLimitInformation = 9
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

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeJobHandle() : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeJobHandle CreateJobObjectW(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeJobHandle job,
        JobObjectInfoClass infoClass,
        ref JobObjectExtendedLimitInformation information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
