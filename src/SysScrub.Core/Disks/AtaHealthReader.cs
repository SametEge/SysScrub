using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SysScrub.Core.Disks;

/// <summary>Ayrıştırılmış ham ATA özniteliği; ada ve açıklamaya tablo katmanı karar veriyor.</summary>
public readonly record struct RawAtaAttribute(byte Id, byte Current, byte Worst, byte Threshold, long Raw);

/// <summary>
/// ATA/SATA disklerin S.M.A.R.T. öznitelik tablosunu okur.
///
/// NVMe'nin aksine ATA'da standart bir sağlık yapısı yok: disk 30 adete kadar
/// öznitelik bildiriyor ve her özniteliğin anlamını ÜRETİCİ belirliyor. Aynı
/// kimlik bir markada "SSD ömrü", başkasında bambaşka bir şey olabiliyor.
/// Bu yüzden yorumlama koda gömülmüyor, <see cref="SmartAttributeTable"/>
/// tablosundan geliyor.
/// </summary>
public static class AtaHealthReader
{
    /// <summary>Öznitelik tablosunda en fazla 30 giriş var; her giriş 12 bayt.</summary>
    private const int MaxAttributes = 30;

    private const int EntrySize = 12;

    /// <summary>Tablo 2. bayttan başlıyor; ilk iki bayt revizyon numarası.</summary>
    private const int TableOffset = 2;

    public static IReadOnlyList<RawAtaAttribute> TryRead(
        SafeFileHandle handle,
        byte driveNumber,
        out string? message)
    {
        message = null;

        byte[]? values = SendSmartCommand(handle, driveNumber, DiskNative.SmartReadAttributes, out int error);

        if (values is null)
        {
            message = error switch
            {
                5 => "SMART okumak için yönetici hakkı gerekiyor.",
                1 or 50 => "Bu bağlantı üzerinden SMART okunamıyor.",
                _ => $"ATA SMART okunamadı (hata {error})."
            };

            return [];
        }

        // Eşikler ayrı komutla geliyor; okunamazsa öznitelikler yine gösterilir,
        // yalnızca "eşiğin altında mı" sorusunu cevaplayamayız.
        byte[]? thresholds = SendSmartCommand(handle, driveNumber, DiskNative.SmartReadThresholds, out _);

        return Parse(values, thresholds);
    }

    /// <summary>Öznitelik ve eşik tamponlarını birleştirip listeye çevirir.</summary>
    public static IReadOnlyList<RawAtaAttribute> Parse(ReadOnlySpan<byte> values, ReadOnlySpan<byte> thresholds)
    {
        var attributes = new List<RawAtaAttribute>();

        for (int i = 0; i < MaxAttributes; i++)
        {
            int offset = TableOffset + (i * EntrySize);

            if (offset + EntrySize > values.Length)
            {
                break;
            }

            byte id = values[offset];

            // Kimliği sıfır olan giriş kullanılmıyor demek.
            if (id == 0)
            {
                continue;
            }

            attributes.Add(new RawAtaAttribute(
                id,
                values[offset + 3],
                values[offset + 4],
                ThresholdFor(thresholds, id),
                ReadRaw(values.Slice(offset + 5, 6))));
        }

        return attributes;
    }

    /// <summary>Ham değer 48 bit, küçük endian.</summary>
    private static long ReadRaw(ReadOnlySpan<byte> raw)
    {
        long value = 0;

        for (int i = raw.Length - 1; i >= 0; i--)
        {
            value = (value << 8) | raw[i];
        }

        return value;
    }

    private static byte ThresholdFor(ReadOnlySpan<byte> thresholds, byte id)
    {
        for (int i = 0; i < MaxAttributes; i++)
        {
            int offset = TableOffset + (i * EntrySize);

            if (offset + 2 > thresholds.Length)
            {
                break;
            }

            if (thresholds[offset] == id)
            {
                return thresholds[offset + 1];
            }
        }

        return 0;
    }

    /// <summary>
    /// SMART_RCV_DRIVE_DATA çağrısı.
    ///
    /// Giriş SENDCMDINPARAMS, çıkış SENDCMDOUTPARAMS. İki yapı da hizalama
    /// sürprizi taşıdığı için alanlar tek tek bayt konumlarına yazılıyor —
    /// yapı eşlemesine güvenmek yanlış hizalamada sessizce yanlış veri okutur.
    /// </summary>
    private static byte[]? SendSmartCommand(
        SafeFileHandle handle,
        byte driveNumber,
        byte feature,
        out int error)
    {
        error = 0;

        // SENDCMDINPARAMS: 4 + 8 (IDEREGS) + 1 + 3 + 16 = 32 bayt başlık.
        const int inSize = 32;

        // SENDCMDOUTPARAMS: 4 (cBufferSize) + 12 (DRIVERSTATUS) = 16 bayt başlık.
        const int outHeaderSize = 16;
        const int outSize = outHeaderSize + DiskNative.AtaSectorSize;

        IntPtr input = Marshal.AllocHGlobal(inSize);
        IntPtr output = Marshal.AllocHGlobal(outSize);

        try
        {
            for (int i = 0; i < inSize; i++)
            {
                Marshal.WriteByte(input, i, 0);
            }

            for (int i = 0; i < outSize; i++)
            {
                Marshal.WriteByte(output, i, 0);
            }

            Marshal.WriteInt32(input, 0, DiskNative.AtaSectorSize);

            // IDEREGS
            Marshal.WriteByte(input, 4, feature);                          // Features
            Marshal.WriteByte(input, 5, 1);                                // SectorCount
            Marshal.WriteByte(input, 6, 1);                                // SectorNumber
            Marshal.WriteByte(input, 7, DiskNative.SmartCylinderLow);
            Marshal.WriteByte(input, 8, DiskNative.SmartCylinderHigh);
            Marshal.WriteByte(input, 9, (byte)(0xA0 | ((driveNumber & 1) << 4)));
            Marshal.WriteByte(input, 10, DiskNative.SmartCmd);
            Marshal.WriteByte(input, 11, 0);

            Marshal.WriteByte(input, 12, driveNumber);

            bool ok = DiskNative.DeviceIoControl(
                handle,
                DiskNative.SmartRcvDriveData,
                input,
                inSize,
                output,
                outSize,
                out int returned,
                IntPtr.Zero);

            if (!ok)
            {
                error = Marshal.GetLastWin32Error();
                return null;
            }

            // DRIVERSTATUS.bDriverError sıfırdan farklıysa sürücü komutu reddetti.
            if (Marshal.ReadByte(output, 4) != 0 || returned < outHeaderSize + DiskNative.AtaSectorSize)
            {
                error = -1;
                return null;
            }

            var data = new byte[DiskNative.AtaSectorSize];
            Marshal.Copy(output + outHeaderSize, data, 0, data.Length);

            return data;
        }
        finally
        {
            Marshal.FreeHGlobal(input);
            Marshal.FreeHGlobal(output);
        }
    }
}
