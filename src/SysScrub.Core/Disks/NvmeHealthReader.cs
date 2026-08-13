using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SysScrub.Core.Disks;

/// <summary>
/// NVMe sağlık günlüğünü okur (log sayfası 0x02).
///
/// Modern dahili SSD'lerin tamamı bu yoldan okunuyor ve ek sürücü gerekmiyor:
/// Windows'un depolama yığını NVMe komutlarını <c>IOCTL_STORAGE_QUERY_PROPERTY</c>
/// üzerinden geçiriyor.
///
/// ATA disklerin aksine NVMe'de öznitelik/eşik tablosu yok; standart sabit alanlı
/// bir yapı tanımlıyor. Bu yüzden üreticiye özel yorumlama gerekmiyor — okunan
/// her sayının anlamı standartta yazılı.
/// </summary>
public static class NvmeHealthReader
{
    /// <summary>
    /// Sağlık günlüğünü okur. Okunamazsa sebebini döner; hiçbir durumda
    /// uydurma değer üretilmez.
    /// </summary>
    public static NvmeHealth? TryRead(SafeFileHandle handle, out string? message)
    {
        message = null;

        byte[]? log = QueryProtocolData(
            handle,
            DiskNative.NvmeDataTypeLogPage,
            DiskNative.NvmeLogPageHealthInfo,
            DiskNative.NvmeHealthLogSize,
            out int error);

        if (log is null)
        {
            message = error switch
            {
                5 => "SMART okumak için yönetici hakkı gerekiyor.",
                1 or 50 => "Sürücü bu sorguyu desteklemiyor.",
                _ => $"NVMe sağlık günlüğü okunamadı (hata {error})."
            };

            return null;
        }

        return Parse(log);
    }

    /// <summary>Identify Controller yapısından bellenim sürümü ve seri numarası.</summary>
    public static (string? Serial, string? Firmware, string? Model) TryReadIdentity(SafeFileHandle handle)
    {
        byte[]? identify = QueryProtocolData(
            handle,
            DiskNative.NvmeDataTypeIdentify,
            DiskNative.NvmeIdentifyCnsController,
            DiskNative.NvmeIdentifySize,
            out _);

        if (identify is null)
        {
            return (null, null, null);
        }

        return (
            AsciiField(identify, 4, 20),
            AsciiField(identify, 64, 8),
            AsciiField(identify, 24, 40));
    }

    /// <summary>
    /// Sağlık günlüğünün ayrıştırılması. Alan konumları NVMe standardında sabit.
    /// Genelden ayrı olarak sayaçlar 128 bit; pratikte üst 64 bit her zaman sıfır,
    /// yine de taşma olmasın diye alt 64 bit okunuyor.
    /// </summary>
    public static NvmeHealth Parse(ReadOnlySpan<byte> log)
    {
        // Sıcaklık Kelvin cinsinden geliyor. 0 değeri "bildirilmedi" demek;
        // mutlak sıfırı ekranda göstermemek için sıfırda bırakılıyor.
        ushort kelvin = BinaryPrimitives.ReadUInt16LittleEndian(log[1..3]);
        int celsius = kelvin > 0 ? kelvin - 273 : 0;

        var sensors = new List<int>();

        for (int i = 0; i < 8; i++)
        {
            ushort sensor = BinaryPrimitives.ReadUInt16LittleEndian(log.Slice(200 + (i * 2), 2));

            if (sensor > 0)
            {
                sensors.Add(sensor - 273);
            }
        }

        return new NvmeHealth
        {
            CriticalWarning = log[0],
            TemperatureCelsius = celsius,
            AvailableSpare = log[3],
            AvailableSpareThreshold = log[4],
            PercentageUsed = log[5],
            DataUnitsRead = ReadCounter(log, 32),
            DataUnitsWritten = ReadCounter(log, 48),
            PowerCycles = ReadCounter(log, 112),
            PowerOnHours = ReadCounter(log, 128),
            UnsafeShutdowns = ReadCounter(log, 144),
            MediaErrors = ReadCounter(log, 160),
            ErrorLogEntries = ReadCounter(log, 176),
            SensorsCelsius = sensors
        };
    }

    /// <summary>128 bitlik sayacın alt 64 biti. Üst yarı taşarsa uzun.MaxValue'ya sabitlenir.</summary>
    private static long ReadCounter(ReadOnlySpan<byte> log, int offset)
    {
        ulong low = BinaryPrimitives.ReadUInt64LittleEndian(log.Slice(offset, 8));
        ulong high = BinaryPrimitives.ReadUInt64LittleEndian(log.Slice(offset + 8, 8));

        if (high > 0 || low > long.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)low;
    }

    /// <summary>
    /// Protokole özel veri sorgusu.
    ///
    /// Tek bir tampon hem giriş hem çıkış olarak kullanılıyor: giriş
    /// STORAGE_PROPERTY_QUERY, çıkış STORAGE_PROTOCOL_DATA_DESCRIPTOR ve ikisinde de
    /// protokol yapısı 8. bayttan başlıyor.
    /// </summary>
    private static byte[]? QueryProtocolData(
        SafeFileHandle handle,
        uint dataType,
        uint requestValue,
        int dataLength,
        out int error)
    {
        error = 0;

        int bufferSize = DiskNative.PropertyQueryHeaderSize +
                         DiskNative.ProtocolSpecificDataSize +
                         dataLength;

        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            for (int i = 0; i < bufferSize; i++)
            {
                Marshal.WriteByte(buffer, i, 0);
            }

            // STORAGE_PROPERTY_QUERY
            Marshal.WriteInt32(buffer, 0, (int)DiskNative.StorageDeviceProtocolSpecificProperty);
            Marshal.WriteInt32(buffer, 4, (int)DiskNative.PropertyStandardQuery);

            // STORAGE_PROTOCOL_SPECIFIC_DATA — AdditionalParameters alanına yazılıyor.
            int p = DiskNative.PropertyQueryHeaderSize;

            Marshal.WriteInt32(buffer, p + 0, (int)DiskNative.ProtocolTypeNvme);
            Marshal.WriteInt32(buffer, p + 4, (int)dataType);
            Marshal.WriteInt32(buffer, p + 8, (int)requestValue);
            Marshal.WriteInt32(buffer, p + 12, 0);
            // Veri konumu protokol yapısının BAŞINA göre; yapının kendi boyu kadar ileride.
            Marshal.WriteInt32(buffer, p + 16, DiskNative.ProtocolSpecificDataSize);
            Marshal.WriteInt32(buffer, p + 20, dataLength);

            bool ok = DiskNative.DeviceIoControl(
                handle,
                DiskNative.IoctlStorageQueryProperty,
                buffer,
                bufferSize,
                buffer,
                bufferSize,
                out int returned,
                IntPtr.Zero);

            if (!ok)
            {
                error = Marshal.GetLastWin32Error();
                return null;
            }

            // Çıkışta veri, protokol yapısının başından ProtocolDataOffset kadar ileride.
            int offset = Marshal.ReadInt32(buffer, p + 16);
            int length = Marshal.ReadInt32(buffer, p + 20);

            if (offset <= 0 || length <= 0 ||
                DiskNative.PropertyQueryHeaderSize + offset + length > bufferSize ||
                returned < DiskNative.PropertyQueryHeaderSize + offset)
            {
                error = -1;
                return null;
            }

            var data = new byte[length];
            Marshal.Copy(buffer + DiskNative.PropertyQueryHeaderSize + offset, data, 0, length);

            return data;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>NVMe metin alanları boşlukla dolduruluyor ve sonlandırıcı içermiyor.</summary>
    private static string? AsciiField(byte[] buffer, int offset, int length)
    {
        if (offset + length > buffer.Length)
        {
            return null;
        }

        string value = System.Text.Encoding.ASCII.GetString(buffer, offset, length).Trim('\0', ' ');

        return value.Length > 0 ? value : null;
    }
}
