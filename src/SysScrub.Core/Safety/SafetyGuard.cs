using SysScrub.Core.Rules;
using SysScrub.Core.Machine;

namespace SysScrub.Core.Safety;

/// <summary>
/// Projenin en kritik sınıfı. Silinecek her yol, silme anında buradan geçer.
///
/// Tasarım ilkesi: kural motoruna güvenilmez. Kural dosyaları kullanıcı tarafından
/// düzenlenebilir olduğu için, kötü ya da hatalı bir kural bile bu katmanı aşamamalı.
/// Bu yüzden denetim kuralın ne dediğine değil, yolun kendisine bakar.
/// </summary>
public sealed class SafetyGuard
{
    // .NET'in FileAttributes numaralandırması bu iki bayrağı tanımıyor; ham değer kullanılıyor.
    private const int FileAttributeRecallOnOpen = 0x00040000;
    private const int FileAttributeRecallOnDataAccess = 0x00400000;

    private readonly ProtectedPath[] _protectedTrees;

    public SafetyGuard(PathResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        _protectedTrees = BuildProtectedTrees(resolver);
    }

    /// <summary>Altına hiçbir koşulda dokunulmayan ağaçlar. Tanılama ve testler için açık.</summary>
    public IReadOnlyList<string> ProtectedRoots => _protectedTrees.Select(p => p.Path).ToArray();

    /// <summary>
    /// Bir dosyanın silinip silinemeyeceğini söyler.
    /// <paramref name="allowedRoot"/> kuralın çözümlenmiş kökü — yol bunun altında olmak zorunda.
    /// </summary>
    public GuardVerdict InspectFile(string path, string allowedRoot)
    {
        GuardVerdict basic = InspectCommon(path, allowedRoot, out string normalized);

        if (!basic.IsAllowed)
        {
            return basic;
        }

        FileAttributes attributes;

        try
        {
            attributes = File.GetAttributes(normalized);
        }
        catch (FileNotFoundException)
        {
            // Tarama ile silme arasında kaybolmuş. Silinecek bir şey yok, engel de yok.
            return GuardVerdict.Allow;
        }
        catch (DirectoryNotFoundException)
        {
            return GuardVerdict.Allow;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Okunamayan dosyayı silmeye kalkmıyoruz.
            return GuardVerdict.Deny(GuardDenialReason.InvalidPath);
        }

        return InspectAttributes(attributes);
    }

    /// <summary>
    /// Öznitelikleri zaten okunmuş dosyalar için. Tarama motoru dizin dolaşırken
    /// öznitelikleri bedavaya alıyor; diski ikinci kez okumamak için bu aşırı yükleme var.
    /// </summary>
    public GuardVerdict InspectFile(string path, string allowedRoot, FileAttributes attributes)
    {
        GuardVerdict basic = InspectCommon(path, allowedRoot, out _);

        return basic.IsAllowed ? InspectAttributes(attributes) : basic;
    }

    /// <summary>
    /// Bir klasörün silinip silinemeyeceğini söyler. Dosya denetimine ek olarak
    /// klasörün izin verilen kökün kendisi olmadığını da doğrular — kökü silmek,
    /// kuralın hedeflediği yapıyı yok etmek olur.
    /// </summary>
    public GuardVerdict InspectDirectory(string path, string allowedRoot)
    {
        GuardVerdict basic = InspectCommon(path, allowedRoot, out string normalized);

        if (!basic.IsAllowed)
        {
            return basic;
        }

        if (normalized.Equals(PathResolver.Normalize(allowedRoot), StringComparison.OrdinalIgnoreCase))
        {
            return GuardVerdict.Deny(GuardDenialReason.OutsideAllowedRoot);
        }

        // Sürücü kökü hiçbir zaman silinemez.
        if (IsDriveRoot(normalized))
        {
            return GuardVerdict.Deny(GuardDenialReason.ProtectedSystemDirectory);
        }

        try
        {
            return InspectAttributes(File.GetAttributes(normalized));
        }
        catch (DirectoryNotFoundException)
        {
            return GuardVerdict.Allow;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return GuardVerdict.Deny(GuardDenialReason.InvalidPath);
        }
    }

    /// <summary>Bir klasörün içine girilip girilemeyeceği. Bağlantı noktaları izlenmez.</summary>
    public bool CanTraverse(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return !attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private GuardVerdict InspectCommon(string path, string allowedRoot, out string normalized)
    {
        normalized = PathResolver.Normalize(path);

        if (normalized.Length == 0)
        {
            return GuardVerdict.Deny(GuardDenialReason.InvalidPath);
        }

        // UNC ve aygıt yolları: \\sunucu\paylaşım, \\.\PhysicalDrive0, \\?\C:\...
        if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return GuardVerdict.Deny(GuardDenialReason.NonLocalPath);
        }

        if (!Path.IsPathRooted(normalized))
        {
            return GuardVerdict.Deny(GuardDenialReason.InvalidPath);
        }

        // Korumalı ağaçlar, izin verilen kökten önce denetlenir: bir kural yanlışlıkla
        // System32'yi kök göstermiş olsa bile buradan geçemez.
        foreach (ProtectedPath protectedPath in _protectedTrees)
        {
            if (PathResolver.IsUnder(normalized, protectedPath.Path))
            {
                return GuardVerdict.Deny(protectedPath.Reason);
            }
        }

        string normalizedRoot = PathResolver.Normalize(allowedRoot);

        if (normalizedRoot.Length == 0 || !PathResolver.IsUnder(normalized, normalizedRoot))
        {
            return GuardVerdict.Deny(GuardDenialReason.OutsideAllowedRoot);
        }

        return GuardVerdict.Allow;
    }

    /// <summary>
    /// Dosya özniteliklerine bakarak karar verir. Özniteliği zaten elinde olan çağıranlar
    /// (tarama motoru) diski ikinci kez okumadan bunu kullanabilir.
    /// </summary>
    public static GuardVerdict InspectAttributes(FileAttributes attributes)
    {
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return GuardVerdict.Deny(GuardDenialReason.ReparsePoint);
        }

        int raw = (int)attributes;

        if ((raw & FileAttributeRecallOnDataAccess) != 0 ||
            (raw & FileAttributeRecallOnOpen) != 0 ||
            attributes.HasFlag(FileAttributes.Offline))
        {
            return GuardVerdict.Deny(GuardDenialReason.CloudPlaceholder);
        }

        return GuardVerdict.Allow;
    }

    private static bool IsDriveRoot(string normalized) =>
        normalized.Length <= 3 && normalized.Contains(':');

    private static ProtectedPath[] BuildProtectedTrees(PathResolver resolver)
    {
        var trees = new List<ProtectedPath>();

        void AddSystem(PathToken token, string? relative = null)
        {
            foreach (string basePath in resolver.GetBasePaths(token))
            {
                string full = relative is null ? basePath : Path.Combine(basePath, relative);
                trees.Add(new ProtectedPath(PathResolver.Normalize(full), GuardDenialReason.ProtectedSystemDirectory));
            }
        }

        void AddUser(Environment.SpecialFolder folder)
        {
            string value = Environment.GetFolderPath(folder);

            if (!string.IsNullOrWhiteSpace(value))
            {
                trees.Add(new ProtectedPath(PathResolver.Normalize(value), GuardDenialReason.UserContent));
            }
        }

        // ---- İşletim sistemi bileşenleri ----
        AddSystem(PathToken.SystemRoot, "System32");
        AddSystem(PathToken.SystemRoot, "SysWOW64");
        AddSystem(PathToken.SystemRoot, "WinSxS");
        AddSystem(PathToken.SystemRoot, "servicing");
        AddSystem(PathToken.SystemRoot, "assembly");
        AddSystem(PathToken.SystemRoot, "Boot");
        AddSystem(PathToken.SystemRoot, "Fonts");
        AddSystem(PathToken.SystemRoot, "INF");
        AddSystem(PathToken.SystemRoot, "Microsoft.NET");
        AddSystem(PathToken.SystemRoot, "Registration");
        AddSystem(PathToken.SystemRoot, "security");
        AddSystem(PathToken.ProgramFiles);
        AddSystem(PathToken.ProgramFilesX86);

        // ---- Kullanıcının kendi içeriği ----
        AddUser(Environment.SpecialFolder.MyDocuments);
        AddUser(Environment.SpecialFolder.DesktopDirectory);
        AddUser(Environment.SpecialFolder.MyPictures);
        AddUser(Environment.SpecialFolder.MyVideos);
        AddUser(Environment.SpecialFolder.MyMusic);
        AddUser(Environment.SpecialFolder.Favorites);

        // OneDrive klasörü senkronize edilen her şeyi barındırır; tamamı korunur.
        foreach (string variable in (string[])["OneDrive", "OneDriveConsumer", "OneDriveCommercial"])
        {
            string? oneDrive = Environment.GetEnvironmentVariable(variable);

            if (!string.IsNullOrWhiteSpace(oneDrive))
            {
                trees.Add(new ProtectedPath(PathResolver.Normalize(oneDrive), GuardDenialReason.UserContent));
            }
        }

        // ---- Uygulamanın kendi verisi ----
        trees.Add(new ProtectedPath(PathResolver.Normalize(AppPaths.DataDirectory), GuardDenialReason.ApplicationOwnData));
        trees.Add(new ProtectedPath(PathResolver.Normalize(AppPaths.InstallDirectory), GuardDenialReason.ApplicationOwnData));

        return trees
            .Where(p => p.Path.Length > 0)
            .DistinctBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private readonly record struct ProtectedPath(string Path, GuardDenialReason Reason);
}
