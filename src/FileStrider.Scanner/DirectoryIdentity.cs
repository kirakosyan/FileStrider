using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileStrider.Scanner;

internal readonly record struct DirectoryIdentity(ulong Device, ulong FileIdLow, ulong FileIdHigh = 0)
{
    // Query only the selected directory. The OS follows aliases, including linked
    // ancestors, without requiring us to read attributes on each ancestor.
    public static bool TryGet(string path, out DirectoryIdentity identity)
    {
        identity = default;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var extendedPath = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
                    : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
                using var handle = CreateFile(extendedPath, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
                if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, 18, out var information, 24)) return false;
                if (information.FileIdLow == 0 && information.FileIdHigh == 0) return false;
                identity = new(information.VolumeSerialNumber, information.FileIdLow, information.FileIdHigh);
                return true;
            }
            if (OperatingSystem.IsLinux())
            {
                const uint inodeMask = 0x100;
                if (Statx(-100, path, 0, inodeMask, out var information) != 0 || (information.Mask & inodeMask) == 0) return false;
                identity = new(((ulong)information.DeviceMajor << 32) | information.DeviceMinor, information.Inode);
                return true;
            }
            if (OperatingSystem.IsMacOS())
            {
                var result = RuntimeInformation.ProcessArchitecture == Architecture.X64
                    ? StatMacX64(path, out var information) : StatMacArm64(path, out information);
                if (result != 0) return false;
                identity = new(information.Device, information.Inode);
                return true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // An unavailable metadata API must not prevent scanning readable folders.
            System.Diagnostics.Trace.TraceWarning($"Directory identity is unavailable: {ex.Message}");
        }
        return false;
    }

    // FILE_ID_INFO: volume serial plus the complete 128-bit file ID.
    // https://learn.microsoft.com/windows/win32/api/winbase/ns-winbase-file_id_info
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileId
    {
        public ulong VolumeSerialNumber, FileIdLow, FileIdHigh;
    }

    // statx is a fixed 256-byte ABI on Linux x64 and ARM64; request the inode only.
    // https://github.com/torvalds/linux/blob/master/include/uapi/linux/stat.h
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }

    // Darwin's 64-bit stat ABI is 144 bytes on both supported architectures.
    // https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/stat.h
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct MacStat
    {
        [FieldOffset(0)] public uint Device;
        [FieldOffset(8)] public ulong Inode;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out WindowsFileId information, uint size);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags, uint mask, out LinuxStatx information);

    [DllImport("libSystem.B.dylib", EntryPoint = "stat$INODE64", SetLastError = true)]
    private static extern int StatMacX64([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out MacStat information);

    [DllImport("libSystem.B.dylib", EntryPoint = "stat", SetLastError = true)]
    private static extern int StatMacArm64([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out MacStat information);
}
