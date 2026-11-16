using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace LargeFileSort.Generator;

internal static class AppInfo
{
    public static string CurrentDirectory => Directory.GetCurrentDirectory();

    public static long GetAvailableFreeSpace(string outputPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath, CurrentDirectory));
        if (!OperatingSystem.IsWindows())
        {
            return new DriveInfo(directory).AvailableFreeSpace;
        }

        directory = Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
        if (!GetDiskFreeSpaceEx(directory, out ulong availableBytes, out _, out _))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return checked((long)availableBytes);
    }

    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(
        string directoryName,
        out ulong freeBytesAvailable,
        out ulong totalBytes,
        out ulong totalFreeBytes);
}
