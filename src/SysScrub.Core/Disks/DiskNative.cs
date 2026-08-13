using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SysScrub.Core.Disks;

/// <summary>
/// Fiziksel disk erişimi için Win32 tanımları.
///
/// SMART verisinin yönetilen bir karşılığı yok; sürücüye doğrudan denetim kodu
/// göndermek zorunlu. Erişim iki kademeli:
///
///   Kimlik (model, seri no, veri yolu) → sıfır erişim hakkıyla açılan tanıtıcı
///   yeterli, yönetici gerektirmiyor.
///
///   Sağlık verisi → sürücüye komut göndermek okuma/yazma hakkı istiyor,
///   bu da yönetici demek. Uygulama yönetici olarak çalıştığı için sorun değil;
///   olmadığında sebebi açıkça yazılıyor.
/// </summary>
internal static class DiskNative
{
    public const uint IoctlStorageQueryProperty = 0x002D_1400;
    public const uint IoctlStorageGetDeviceNumber = 0x002D_1080;
    /// <summary>Kapasite için: uzunluk sorgusu okuma hakkı istiyor, geometri istemiyor.</summary>
    public const uint IoctlDiskGetDriveGeometryEx = 0x0007_00A0;

    /// <summary>DISK_GEOMETRY_EX içinde DiskSize alanının konumu.</summary>
    public const int DiskGeometryExSizeOffset = 24;
    public const uint SmartGetVersion = 0x0007_4080;
    public const uint SmartRcvDriveData = 0x0007_C088;

    public const uint GenericRead = 0x8000_0000;
    public const uint GenericWrite = 0x4000_0000;
    public const uint FileShareRead = 0x0000_0001;
    public const uint FileShareWrite = 0x0000_0002;
    public const uint OpenExisting = 3;

    // ---------------------------------------------------------------- STORAGE_PROPERTY_QUERY

    public const uint StorageDeviceProperty = 0;
    public const uint StorageDeviceSeekPenaltyProperty = 7;
    public const uint StorageDeviceProtocolSpecificProperty = 50;
    public const uint PropertyStandardQuery = 0;

    // ---------------------------------------------------------------- NVMe protokolü

    public const uint ProtocolTypeNvme = 3;
    public const uint NvmeDataTypeIdentify = 1;
    public const uint NvmeDataTypeLogPage = 2;

    /// <summary>Identify Controller yapısı.</summary>
    public const uint NvmeIdentifyCnsController = 1;

    /// <summary>SMART / sağlık bilgisi günlük sayfası.</summary>
    public const uint NvmeLogPageHealthInfo = 2;

    /// <summary>STORAGE_PROTOCOL_SPECIFIC_DATA yapısının bayt uzunluğu.</summary>
    public const int ProtocolSpecificDataSize = 40;

    /// <summary>STORAGE_PROPERTY_QUERY başlığı: PropertyId + QueryType.</summary>
    public const int PropertyQueryHeaderSize = 8;

    /// <summary>NVMe sağlık günlüğü 512, Identify 4096 bayt.</summary>
    public const int NvmeHealthLogSize = 512;

    public const int NvmeIdentifySize = 4096;

    // ---------------------------------------------------------------- ATA SMART

    public const byte SmartCmd = 0xB0;
    public const byte SmartReadAttributes = 0xD0;
    public const byte SmartReadThresholds = 0xD1;
    public const byte SmartCylinderLow = 0x4F;
    public const byte SmartCylinderHigh = 0xC2;

    /// <summary>SENDCMDOUTPARAMS içindeki veri alanının uzunluğu.</summary>
    public const int AtaSectorSize = 512;

    [StructLayout(LayoutKind.Sequential)]
    public struct StorageDeviceDescriptor
    {
        public uint Version;
        public uint Size;
        public byte DeviceType;
        public byte DeviceTypeModifier;
        [MarshalAs(UnmanagedType.U1)]
        public bool RemovableMedia;
        [MarshalAs(UnmanagedType.U1)]
        public bool CommandQueueing;
        public uint VendorIdOffset;
        public uint ProductIdOffset;
        public uint ProductRevisionOffset;
        public uint SerialNumberOffset;
        public uint BusType;
        public uint RawPropertiesLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceSeekPenaltyDescriptor
    {
        public uint Version;
        public uint Size;
        [MarshalAs(UnmanagedType.U1)]
        public bool IncursSeekPenalty;
    }

    /// <summary>SMART_RCV_DRIVE_DATA girişi.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SendCmdInParams
    {
        public uint BufferSize;
        public IdeRegisters IrDriveRegs;
        public byte DriveNumber;
        public byte Reserved1;
        public byte Reserved2;
        public byte Reserved3;
        public uint Reserved4;
        public uint Reserved5;
        public uint Reserved6;
        public uint Reserved7;
        public byte Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IdeRegisters
    {
        public byte Features;
        public byte SectorCount;
        public byte SectorNumber;
        public byte CylinderLow;
        public byte CylinderHigh;
        public byte DriveHead;
        public byte Command;
        public byte Reserved;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inBuffer,
        int inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out int bytesReturned,
        IntPtr overlapped);
}
