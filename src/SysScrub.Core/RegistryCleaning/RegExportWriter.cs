using System.Globalization;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace SysScrub.Core.RegistryCleaning;

/// <summary>
/// Silinecek anahtar ve değerleri geçerli bir .reg dosyasına yazar.
///
/// reg.exe export kullanılmıyor: o yalnızca anahtarın tamamını dışa aktarabiliyor,
/// tek bir değeri değil. Bizim çoğu bulgumuz büyük bir anahtarın içindeki tek değer;
/// tüm anahtarı yedeklemek hem gereksiz büyük hem de geri yüklerken kullanıcının
/// aradan yaptığı değişiklikleri geri sarma riski taşıyor.
///
/// Üretilen dosya regedit ve reg.exe ile birebir uyumlu.
/// </summary>
public static class RegExportWriter
{
    private const string Header = "Windows Registry Editor Version 5.00";

    /// <summary>
    /// Verilen konumları tek bir .reg dosyasına yazar. Aynı anahtardaki değerler
    /// tek blokta toplanır. Okunamayan konumlar sessizce atlanır — yedeklenemeyen
    /// bir şey zaten silinmeyecek.
    /// </summary>
    public static void Write(string path, IEnumerable<RegistryLocation> locations)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Header);
        builder.AppendLine();

        // Anahtar başına grupla: aynı anahtarda birden çok değer varsa tek blok yeter.
        var groups = locations
            .GroupBy(l => (l.Hive, l.View, Key: l.KeyPath), KeyComparer.Instance)
            .OrderBy(g => g.Key.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            RegistryLocation first = group.First();
            bool wholeKey = group.Any(l => l.TargetsWholeKey);

            using RegistryKey? key = OpenKey(first);

            if (key is null)
            {
                continue;
            }

            if (wholeKey)
            {
                WriteKeyRecursive(builder, key, first.FullPath);
                continue;
            }

            builder.AppendLine($"[{first.FullPath}]");

            foreach (RegistryLocation location in group.Where(l => !l.TargetsWholeKey))
            {
                AppendValue(builder, key, location.ValueName!);
            }

            builder.AppendLine();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Regedit UTF-16 LE bekliyor; ASCII yazılırsa Türkçe anahtar adları bozulur.
        File.WriteAllText(path, builder.ToString(), new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
    }

    private static RegistryKey? OpenKey(RegistryLocation location)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(location.Hive, location.View);
            return baseKey.OpenSubKey(location.KeyPath, writable: false);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static void WriteKeyRecursive(StringBuilder builder, RegistryKey key, string fullPath)
    {
        builder.AppendLine($"[{fullPath}]");

        foreach (string valueName in SafeValueNames(key))
        {
            AppendValue(builder, key, valueName);
        }

        builder.AppendLine();

        foreach (string subKeyName in SafeSubKeyNames(key))
        {
            using RegistryKey? subKey = TryOpenSubKey(key, subKeyName);

            if (subKey is not null)
            {
                WriteKeyRecursive(builder, subKey, $"{fullPath}\\{subKeyName}");
            }
        }
    }

    private static void AppendValue(StringBuilder builder, RegistryKey key, string valueName)
    {
        object? value;
        RegistryValueKind kind;

        try
        {
            // DoNotExpandEnvironmentNames: %SystemRoot% gibi ifadeler yedekte olduğu gibi kalmalı.
            value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            kind = key.GetValueKind(valueName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return;
        }

        if (value is null)
        {
            return;
        }

        // Varsayılan değer "@" ile gösterilir.
        string name = valueName.Length == 0 ? "@" : $"\"{EscapeString(valueName)}\"";

        switch (kind)
        {
            case RegistryValueKind.String:
                builder.AppendLine($"{name}=\"{EscapeString(value.ToString() ?? string.Empty)}\"");
                break;

            case RegistryValueKind.ExpandString:
                builder.AppendLine($"{name}=hex(2):{HexOfString(value.ToString() ?? string.Empty)}");
                break;

            case RegistryValueKind.MultiString:
                builder.AppendLine($"{name}=hex(7):{HexOfMultiString((string[])value)}");
                break;

            case RegistryValueKind.DWord:
                builder.AppendLine(
                    $"{name}=dword:{unchecked((uint)Convert.ToInt32(value, CultureInfo.InvariantCulture)):x8}");
                break;

            case RegistryValueKind.QWord:
                builder.AppendLine(
                    $"{name}=hex(b):{HexOfBytes(BitConverter.GetBytes(Convert.ToInt64(value, CultureInfo.InvariantCulture)))}");
                break;

            case RegistryValueKind.Binary:
                builder.AppendLine($"{name}=hex:{HexOfBytes((byte[])value)}");
                break;

            default:
                // Bilinmeyen tür: yedeklenemiyorsa hiç yazmıyoruz, silme de engellenecek.
                break;
        }
    }

    /// <summary>.reg dizelerinde ters eğik çizgi ve tırnak kaçırılmak zorunda.</summary>
    private static string EscapeString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string HexOfString(string value) =>
        HexOfBytes(Encoding.Unicode.GetBytes(value + '\0'));

    private static string HexOfMultiString(string[] values)
    {
        var builder = new StringBuilder();

        foreach (string value in values)
        {
            builder.Append(value).Append('\0');
        }

        // Çift null ile biter.
        builder.Append('\0');

        return HexOfBytes(Encoding.Unicode.GetBytes(builder.ToString()));
    }

    private static string HexOfBytes(byte[] bytes) =>
        string.Join(',', bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));

    private static IEnumerable<string> SafeValueNames(RegistryKey key)
    {
        try
        {
            return key.GetValueNames();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeSubKeyNames(RegistryKey key)
    {
        try
        {
            return key.GetSubKeyNames();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return [];
        }
    }

    private static RegistryKey? TryOpenSubKey(RegistryKey key, string name)
    {
        try
        {
            return key.OpenSubKey(name, writable: false);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private sealed class KeyComparer : IEqualityComparer<(RegistryHive Hive, RegistryView View, string Key)>
    {
        public static KeyComparer Instance { get; } = new();

        public bool Equals((RegistryHive Hive, RegistryView View, string Key) x,
                           (RegistryHive Hive, RegistryView View, string Key) y) =>
            x.Hive == y.Hive && x.View == y.View &&
            string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((RegistryHive Hive, RegistryView View, string Key) obj) =>
            HashCode.Combine(obj.Hive, obj.View, obj.Key.ToUpperInvariant());
    }
}
