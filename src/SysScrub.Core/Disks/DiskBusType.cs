namespace SysScrub.Core.Disks;

/// <summary>
/// Windows'un veri yolu türleri (STORAGE_BUS_TYPE).
///
/// Numaralandırma ntddstor.h'deki sırayla birebir olmak zorunda: bir kayma
/// NVMe diski "Storage Spaces" sanmaya ve sağlık verisini yanlış okuyucuya
/// göndermeye yol açıyor.
/// </summary>
public static class DiskBusType
{
    public const uint Usb = 0x07;
    public const uint Sata = 0x0B;
    public const uint Spaces = 0x10;
    public const uint Nvme = 0x11;

    public static string Describe(uint busType) => busType switch
    {
        0x01 => "SCSI",
        0x02 => "ATAPI",
        0x03 => "ATA",
        0x04 => "IEEE 1394",
        0x05 => "SSA",
        0x06 => "Fibre Channel",
        Usb => "USB",
        0x08 => "RAID",
        0x09 => "iSCSI",
        0x0A => "SAS",
        Sata => "SATA",
        0x0C => "SD",
        0x0D => "MMC",
        0x0E => "Sanal",
        0x0F => "Dosya tabanlı sanal",
        Spaces => "Storage Spaces",
        Nvme => "NVMe",
        0x12 => "SCM",
        0x13 => "UFS",
        _ => "Bilinmiyor"
    };
}
