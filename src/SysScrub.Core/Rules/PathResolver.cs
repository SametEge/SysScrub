using System.Collections.Concurrent;

namespace SysScrub.Core.Rules;

/// <summary>
/// Sembolik kökleri gerçek yollara çevirir ve joker karakterli segmentleri genişletir.
///
/// Örnek: <c>LocalAppData</c> + <c>"Google/Chrome/User Data/*/Cache"</c> ifadesi,
/// makinede kaç Chrome profili varsa o kadar gerçek klasöre açılır.
///
/// Sonuçlar önbelleklenir: bir tarama sırasında aynı token defalarca sorgulanıyor.
/// </summary>
public sealed class PathResolver
{
    private readonly ConcurrentDictionary<PathToken, IReadOnlyList<string>> _baseCache = new();

    /// <summary>
    /// Bir kökü ve altındaki göreli yolu var olan klasörlere çevirir.
    /// Var olmayan yollar sonuçta yer almaz — taranacak bir şey yok demektir.
    /// </summary>
    public IReadOnlyList<string> Resolve(PathToken token, string? relativePath = null)
    {
        IReadOnlyList<string> current = GetBasePaths(token);

        if (current.Count == 0 || string.IsNullOrWhiteSpace(relativePath))
        {
            return current.Where(Directory.Exists).ToArray();
        }

        foreach (string segment in SplitSegments(relativePath))
        {
            current = Expand(current, segment);

            if (current.Count == 0)
            {
                return [];
            }
        }

        return current;
    }

    /// <summary>Bir token'ın taban klasörleri. AllFixedDrives dışında hepsi tek elemanlı.</summary>
    public IReadOnlyList<string> GetBasePaths(PathToken token) =>
        _baseCache.GetOrAdd(token, ResolveBasePaths);

    /// <summary>
    /// Bir yolun izin verilen köklerden birinin altında olup olmadığını söyler.
    /// SafetyGuard'ın kök denetimi buna dayanır.
    /// </summary>
    public static bool IsUnder(string path, string root)
    {
        string normalizedPath = Normalize(path);
        string normalizedRoot = Normalize(root);

        if (normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Ayırıcı eklenmezse "C:\Program" yolu "C:\Program Files" kökünün altında sanılır.
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            normalizedRoot += Path.DirectorySeparatorChar;
        }

        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Yolu tam, normalize edilmiş ve sondaki ayırıcısı kırpılmış biçime getirir.</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            string full = Path.GetFullPath(path);

            // Sürücü kökünde ("C:\") sondaki ayırıcı anlamlı, kırpılmaz.
            return full.Length > 3 ? full.TrimEnd(Path.DirectorySeparatorChar) : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    private static IEnumerable<string> SplitSegments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> Expand(IReadOnlyList<string> directories, string segment)
    {
        bool hasWildcard = segment.Contains('*') || segment.Contains('?');
        var result = new List<string>();

        foreach (string directory in directories)
        {
            if (!hasWildcard)
            {
                string candidate = Path.Combine(directory, segment);

                if (Directory.Exists(candidate))
                {
                    result.Add(candidate);
                }

                continue;
            }

            // Joker segment: yalnızca bu seviyedeki klasörlere bakılır, ağaç dolaşılmaz.
            try
            {
                result.AddRange(Directory.EnumerateDirectories(directory, segment, SearchOption.TopDirectoryOnly));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                // Erişilemeyen klasör taranamaz; sessizce atlanır.
            }
        }

        return result;
    }

    private static IReadOnlyList<string> ResolveBasePaths(PathToken token)
    {
        string? path = token switch
        {
            PathToken.UserTemp => Path.GetTempPath(),
            PathToken.WindowsTemp => Path.Combine(GetSystemRoot(), "Temp"),
            PathToken.LocalAppData => Folder(Environment.SpecialFolder.LocalApplicationData),
            PathToken.RoamingAppData => Folder(Environment.SpecialFolder.ApplicationData),
            PathToken.ProgramData => Folder(Environment.SpecialFolder.CommonApplicationData),
            PathToken.UserProfile => Folder(Environment.SpecialFolder.UserProfile),
            PathToken.SystemRoot => GetSystemRoot(),
            PathToken.SystemDrive => Path.GetPathRoot(GetSystemRoot()),
            PathToken.ProgramFiles => Folder(Environment.SpecialFolder.ProgramFiles),
            PathToken.ProgramFilesX86 => Folder(Environment.SpecialFolder.ProgramFilesX86),
            PathToken.Downloads => KnownFolders.GetDownloads(),
            PathToken.Documents => Folder(Environment.SpecialFolder.MyDocuments),
            PathToken.Desktop => Folder(Environment.SpecialFolder.DesktopDirectory),
            PathToken.AllFixedDrives => null,
            _ => null
        };

        if (token == PathToken.AllFixedDrives)
        {
            return FixedDriveRoots();
        }

        return string.IsNullOrWhiteSpace(path) ? [] : [Normalize(path)];

        static string? Folder(Environment.SpecialFolder folder)
        {
            string value = Environment.GetFolderPath(folder);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    private static string GetSystemRoot() =>
        Environment.GetEnvironmentVariable("SystemRoot")
        ?? Path.GetDirectoryName(Environment.SystemDirectory)
        ?? @"C:\Windows";

    private static IReadOnlyList<string> FixedDriveRoots()
    {
        var roots = new List<string>();

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                {
                    roots.Add(drive.RootDirectory.FullName);
                }
            }
            catch (IOException)
            {
                // Sorgulanırken kaybolan sürücü.
            }
        }

        return roots;
    }
}
