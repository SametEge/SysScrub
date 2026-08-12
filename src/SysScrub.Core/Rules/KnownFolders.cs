using System.Runtime.InteropServices;

namespace SysScrub.Core.Rules;

/// <summary>
/// Windows'un bilinen klasörlerini okur.
///
/// Environment.SpecialFolder bazılarını (İndirilenler) hiç bilmiyor, bazılarını da
/// kullanıcı taşımışsa yanlış bildiriyor. SHGetKnownFolderPath her durumda doğru yolu verir.
/// </summary>
internal static class KnownFolders
{
    private static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string? GetDownloads() => Get(Downloads);

    private static string? Get(Guid folderId)
    {
        nint buffer = nint.Zero;

        try
        {
            if (SHGetKnownFolderPath(folderId, 0, nint.Zero, out buffer) != 0)
            {
                return null;
            }

            return Marshal.PtrToStringUni(buffer);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (buffer != nint.Zero)
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid folderId,
        uint flags,
        nint token,
        out nint path);
}
