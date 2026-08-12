using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;

namespace SysScrub.Core.System;

/// <summary>
/// Sistemin anlık durumunu okur. Hiçbir şeyi değiştirmez, yönetici hakkı gerektirmez —
/// panel açılışta bunu çağırır ve arka plan izleyicisi periyodik olarak tazeler.
/// </summary>
public sealed class SystemInfoService
{
    public SystemSnapshot Capture()
    {
        return new SystemSnapshot
        {
            OperatingSystem = DescribeWindows(),
            MachineName = Environment.MachineName,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            IsElevated = IsProcessElevated(),
            Drives = ReadDrives(),
            Memory = ReadMemory()
        };
    }

    private static IReadOnlyList<DriveSnapshot> ReadDrives()
    {
        var result = new List<DriveSnapshot>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            // Hazır olmayan (boş optik, bağlı olmayan ağ) sürücüler sorgulandığında hata verir.
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
            {
                continue;
            }

            try
            {
                result.Add(new DriveSnapshot
                {
                    Name = drive.Name,
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Yerel disk" : drive.VolumeLabel,
                    Format = drive.DriveFormat,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.AvailableFreeSpace
                });
            }
            catch (IOException)
            {
                // Sürücü okuma anında çıkarıldıysa sessizce atla.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return result;
    }

    private static MemorySnapshot ReadMemory()
    {
        var status = MemoryStatusEx.Create();

        if (!GlobalMemoryStatusEx(ref status))
        {
            return new MemorySnapshot { TotalBytes = 0, AvailableBytes = 0 };
        }

        return new MemorySnapshot
        {
            TotalBytes = status.TotalPhys,
            AvailableBytes = status.AvailPhys
        };
    }

    /// <summary>
    /// Environment.OSVersion Windows 11'i de "10.0" olarak bildiriyor; gerçek pazarlama adı
    /// için derleme numarasına bakmak gerekiyor (22000 ve üstü = Windows 11).
    /// </summary>
    private static string DescribeWindows()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

            if (key is null)
            {
                return Environment.OSVersion.VersionString;
            }

            string product = key.GetValue("ProductName") as string ?? "Windows";
            string display = key.GetValue("DisplayVersion") as string ?? string.Empty;
            string build = key.GetValue("CurrentBuildNumber") as string ?? "0";

            if (int.TryParse(build, out int buildNumber) && buildNumber >= 22000)
            {
                product = product.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
            }

            return string.IsNullOrEmpty(display)
                ? $"{product} (derleme {build})"
                : $"{product} {display} (derleme {build})";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Environment.OSVersion.VersionString;
        }
    }

    private static bool IsProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;

        public static MemoryStatusEx Create() => new()
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
