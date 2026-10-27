using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StudioUtil.Testing;

internal static class WorkerStandardInput
{
    public static SafeFileHandle? IsolateChildProcesses()
    {
        if (!OperatingSystem.IsWindows()) return null;
        // The protocol reader retains its original pipe. Native children must not inherit
        // that pipe: GetFileType/_setmode can block behind its pending synchronous read.
        var input = CreateFile("NUL", 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (input.IsInvalid) { input.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        if (SetStdHandle(-10, input)) return input;
        int error = Marshal.GetLastWin32Error();
        input.Dispose();
        throw new Win32Exception(error);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int kind, SafeFileHandle handle);
}
