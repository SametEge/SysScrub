namespace SysScrub.Core.Machine;

/// <summary>
/// Uygulamanın yazdığı her yolun tek kaynağı.
///
/// Portatif mod: çalıştırılabilir dosyanın yanında "portable.flag" varsa tüm veri
/// uygulamanın kendi klasöründe tutulur ve sisteme hiçbir şey yazılmaz. USB'den
/// çalıştıran teknisyenler için bu şart.
/// </summary>
public static class AppPaths
{
    private const string PortableMarker = "portable.flag";

    private static readonly Lazy<string> Root = new(ResolveRoot);

    public static bool IsPortable { get; private set; }

    /// <summary>Günlükler, karantina, yedekler ve ayarların kökü.</summary>
    public static string DataDirectory => Root.Value;

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string QuarantineDirectory => Path.Combine(DataDirectory, "quarantine");

    public static string BackupsDirectory => Path.Combine(DataDirectory, "backups");

    public static string RulesDirectory => Path.Combine(DataDirectory, "rules");

    /// <summary>
    /// Tek dosya yayınında Assembly.Location boş döner; BaseDirectory her iki durumda da doğru.
    /// </summary>
    public static string InstallDirectory => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    private static string ResolveRoot()
    {
        string installDirectory = InstallDirectory;

        if (File.Exists(Path.Combine(installDirectory, PortableMarker)))
        {
            IsPortable = true;
            return Path.Combine(installDirectory, "Data");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SysScrub");
    }
}
