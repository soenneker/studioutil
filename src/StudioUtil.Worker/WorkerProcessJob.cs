using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StudioUtil.Testing;

internal static class WorkerProcessJob
{
    // Rooted for the worker lifetime. Windows closes the handle on normal exit or
    // crash, terminating all descendants even if managed cleanup cannot run.
    private static SafeFileHandle? _job;

    public static void Initialize()
    {
        if (!OperatingSystem.IsWindows()) return;
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new byte[IntPtr.Size == 8 ? 144 : 112];
        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION.BasicLimitInformation.LimitFlags.
        BitConverter.GetBytes(0x2000u).CopyTo(limits, 16); // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(job, 9, limits, (uint)limits.Length) || !AssignProcessToJobObject(job, new IntPtr(-1)))
        {
            int error = Marshal.GetLastWin32Error(); job.Dispose(); throw new Win32Exception(error);
        }
        _job = job;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, byte[] information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
