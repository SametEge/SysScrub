using System.Runtime.InteropServices;
using System.Text;

namespace SysScrub.Core.Windows;

/// <summary>
/// Dolaylı dize çözümleyici.
///
/// Windows, yerelleştirilmiş metinleri registry'de düz metin olarak tutmuyor;
/// "@C:\...\MpAsDesc.dll,-245" gibi bir kaynak göstergesi yazıyor. Çözülmezse
/// kullanıcı servis adı yerine bu ham göstergeyi görür.
///
/// Yönetilen karşılığı yok; kabuk API'si kullanmak zorunlu.
/// </summary>
public static class IndirectString
{
    private const int MaxLength = 1024;

    /// <summary>
    /// Değer dolaylı bir dizeyse çözer, değilse olduğu gibi döner.
    /// Çözülemezse de ham değeri döndürüyoruz: elimizdeki tek metin o.
    /// </summary>
    public static string Resolve(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('@'))
        {
            return value;
        }

        var buffer = new StringBuilder(MaxLength);

        try
        {
            return SHLoadIndirectString(value, buffer, buffer.Capacity, IntPtr.Zero) == 0 && buffer.Length > 0
                ? buffer.ToString()
                : value;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return value;
        }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(
        string source,
        StringBuilder output,
        int outputLength,
        IntPtr reserved);
}
