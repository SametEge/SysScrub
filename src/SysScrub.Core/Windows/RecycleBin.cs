using System.Runtime.InteropServices;

namespace SysScrub.Core.Windows;

/// <summary>Geri Dönüşüm Kutusu'nun boyutu ve boşaltılması.</summary>
public readonly record struct RecycleBinInfo(long Bytes, long ItemCount)
{
    public bool IsEmpty => ItemCount == 0;
}

/// <summary>
/// Geri Dönüşüm Kutusu kabuk API'siyle yönetilir. $Recycle.Bin klasörünü elle dolaşmak
/// mümkün ama yanlış: kutu, kullanıcı başına ayrı izinlere ve dosya adı eşlemesine sahip;
/// kabuk API'si bunların hepsini doğru şekilde halleder.
/// </summary>
public static class RecycleBin
{
    private const int NoProgressUi = 0x00000001;
    private const int NoConfirmation = 0x00000002;
    private const int NoSound = 0x00000004;

    /// <summary>Tüm sürücülerdeki toplam boyut ve öğe sayısı.</summary>
    public static RecycleBinInfo Query(string? driveRoot = null)
    {
        var info = new ShQueryRbInfo { Size = Marshal.SizeOf<ShQueryRbInfo>() };

        try
        {
            // S_OK dışında bir değer, o sürücüde kutu olmadığı anlamına gelir.
            return SHQueryRecycleBin(driveRoot, ref info) == 0
                ? new RecycleBinInfo(info.Size64, info.NumItems64)
                : default;
        }
        catch (DllNotFoundException)
        {
            return default;
        }
        catch (EntryPointNotFoundException)
        {
            return default;
        }
    }

    /// <summary>Kutuyu boşaltır. Onay ve ilerleme penceresi gösterilmez; onayı biz aldık.</summary>
    public static bool Empty(string? driveRoot = null)
    {
        try
        {
            return SHEmptyRecycleBin(nint.Zero, driveRoot, NoConfirmation | NoProgressUi | NoSound) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ShQueryRbInfo
    {
        public int Size;
        public long Size64;
        public long NumItems64;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(nint owner, string? rootPath, int flags);
}
