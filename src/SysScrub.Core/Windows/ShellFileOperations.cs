using System.Runtime.InteropServices;

namespace SysScrub.Core.Windows;

/// <summary>
/// Geri Dönüşüm Kutusu'na silme. File.Delete kalıcı siler; kutuya göndermenin
/// yönetilen bir karşılığı yok, kabuk API'si kullanmak zorunlu.
/// </summary>
public static class ShellFileOperations
{
    private const uint OperationDelete = 0x0003;

    private const ushort AllowUndo = 0x0040;        // Geri Dönüşüm Kutusu'na gönder
    private const ushort NoConfirmation = 0x0010;   // onayı zaten aldık
    private const ushort Silent = 0x0004;           // ilerleme penceresi gösterme
    private const ushort NoErrorUi = 0x0400;        // hatayı biz raporluyoruz
    private const ushort NoConfirmMkDir = 0x0200;

    /// <summary>
    /// Verilen yolları Geri Dönüşüm Kutusu'na taşır.
    /// Kabuk API'si çift null ile biten liste bekliyor.
    /// </summary>
    public static bool DeleteToRecycleBin(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0)
        {
            return true;
        }

        string packed = string.Join('\0', paths) + "\0\0";

        var operation = new ShFileOpStruct
        {
            Func = OperationDelete,
            From = packed,
            Flags = AllowUndo | NoConfirmation | Silent | NoErrorUi | NoConfirmMkDir
        };

        try
        {
            return SHFileOperation(ref operation) == 0 && !operation.AnyOperationsAborted;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct ShFileOpStruct
    {
        public nint Window;
        public uint Func;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string From;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? To;

        public ushort Flags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;

        public nint NameMappings;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct fileOp);
}
