using System.Diagnostics;
using System.Globalization;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SysScrub.Core.RegistryCleaning;
using SysScrub.Core.Windows;

namespace SysScrub.Core.Programs;

/// <summary>
/// Kurulu programları listeler.
///
/// Kaynaklar: Uninstall kayıtları (HKLM 64 ve 32 bit görünüm + HKCU) ve
/// Store paket deposu. <c>Win32_Product</c> WMI sınıfı BİLEREK kullanılmıyor:
/// sorgulandığında her MSI paketini yeniden yapılandırıyor, dakikalarca sürüyor
/// ve olay günlüğünü şişiriyor. Microsoft da kullanılmamasını öneriyor.
/// </summary>
public sealed class ProgramInventory(ILogger<ProgramInventory>? logger = null)
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private const string StorePackagesPath =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    private readonly ILogger _logger = logger ?? NullLogger<ProgramInventory>.Instance;

    public async Task<ProgramInventoryReport> LoadAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        List<InstalledProgram> programs = await Task
            .Run(() => Collect(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();

        _logger.LogInformation(
            "Program envanteri: {Total} kayıt ({Visible} görünür), {Elapsed} ms",
            programs.Count, programs.Count(p => !p.IsSystemComponent), stopwatch.ElapsedMilliseconds);

        return new ProgramInventoryReport
        {
            Programs = programs
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            Duration = stopwatch.Elapsed
        };
    }

    private List<InstalledProgram> Collect(CancellationToken cancellationToken)
    {
        var programs = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectRegistry(RegistryHive.LocalMachine, RegistryView.Registry64, programs, seen, cancellationToken);
        CollectRegistry(RegistryHive.LocalMachine, RegistryView.Registry32, programs, seen, cancellationToken);
        CollectRegistry(RegistryHive.CurrentUser, RegistryView.Registry64, programs, seen, cancellationToken);

        CollectStore(programs, cancellationToken);

        return programs;
    }

    // ------------------------------------------------------------------ Uninstall kayıtları

    private void CollectRegistry(
        RegistryHive hive,
        RegistryView view,
        List<InstalledProgram> programs,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        using RegistryKey? root = RegistryReader.OpenKey(hive, view, UninstallPath);

        foreach (string keyName in RegistryReader.SubKeyNames(root))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using RegistryKey? key = RegistryReader.OpenSubKey(root, keyName);

            if (key is null)
            {
                continue;
            }

            InstalledProgram? program = ReadEntry(key, hive, view, keyName);

            if (program is null)
            {
                continue;
            }

            // 64 ve 32 bit görünüm aynı fiziksel anahtarı gösterebiliyor; iki kez listelemeyiz.
            if (!seen.Add($"{hive}|{keyName}"))
            {
                continue;
            }

            programs.Add(program);
        }
    }

    private static InstalledProgram? ReadEntry(
        RegistryKey key,
        RegistryHive hive,
        RegistryView view,
        string keyName)
    {
        try
        {
            string? name = RegistryReader.StringValue(key, "DisplayName")?.Trim();

            // Adı olmayan kayıt kullanıcıya gösterilebilir bir program değil.
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            // ParentKeyName taşıyan kayıtlar yama/güncelleme; ana programın altında yaşıyorlar.
            if (RegistryReader.StringValue(key, "ParentKeyName") is { Length: > 0 })
            {
                return null;
            }

            if (RegistryReader.StringValue(key, "ReleaseType") is { } release &&
                release.Contains("Update", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string? uninstall = RegistryReader.StringValue(key, "UninstallString")?.Trim();
            string? quiet = RegistryReader.StringValue(key, "QuietUninstallString")?.Trim();

            return new InstalledProgram
            {
                Id = $"reg|{hive}|{view}|{keyName}",
                Name = IndirectString.Resolve(name),
                Source = ProgramSource.Registry,
                Publisher = RegistryReader.StringValue(key, "Publisher")?.Trim(),
                Version = RegistryReader.StringValue(key, "DisplayVersion")?.Trim(),
                InstallDate = ParseInstallDate(RegistryReader.StringValue(key, "InstallDate")),
                InstallLocation = NormalizeLocation(RegistryReader.StringValue(key, "InstallLocation")),
                UninstallCommand = string.IsNullOrWhiteSpace(uninstall) ? null : uninstall,
                QuietUninstallCommand = string.IsNullOrWhiteSpace(quiet) ? null : quiet,
                // Kaldırıcı dosyası kaybolmuşsa program kayıtlı görünür ama kaldırılamaz;
                // kullanıcı düğmeye basıp hata almadan önce bunu bilmeli.
                UninstallerMissing = !string.IsNullOrWhiteSpace(uninstall) &&
                                     !UninstallCommandLine.TargetExists(uninstall),
                // Kayıttaki EstimatedSize kilobayt cinsinden.
                RegistrySizeBytes = key.GetValue("EstimatedSize") is int kilobytes && kilobytes > 0
                    ? (long)kilobytes * 1024
                    : 0,
                IsSystemComponent = key.GetValue("SystemComponent") is int component && component == 1,
                Is32Bit = view == RegistryView.Registry32,
                IsMachineWide = hive == RegistryHive.LocalMachine,
                Hive = hive,
                View = view,
                RegistryKeyPath = $@"{UninstallPath}\{keyName}"
            };
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Kayıttaki tarih "20260803" biçiminde; bozuk yazılmışsa tarih göstermiyoruz.</summary>
    public static DateTime? ParseInstallDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();

        if (DateTime.TryParseExact(
                raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime exact))
        {
            return exact;
        }

        // Bazı kurulumlar yerel biçimde yazıyor.
        return DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime local)
            ? local
            : null;
    }

    /// <summary>Yol sonundaki ters bölü ve tırnaklar karşılaştırmayı bozuyor.</summary>
    public static string? NormalizeLocation(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string value = raw.Trim().Trim('"').TrimEnd('\\');

        return value.Length == 0 ? null : value;
    }

    // ------------------------------------------------------------------ Store paketleri

    /// <summary>
    /// Store paketleri kendi deposunda listeleniyor. WinRT paket yöneticisini
    /// kullanmıyoruz: hedef çatının o API'ye erişimi yok ve depo aynı bilgiyi veriyor.
    /// </summary>
    private void CollectStore(List<InstalledProgram> programs, CancellationToken cancellationToken)
    {
        using RegistryKey? root = RegistryReader.OpenKey(
            RegistryHive.CurrentUser, RegistryView.Registry64, StorePackagesPath);

        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        foreach (string packageId in RegistryReader.SubKeyNames(root))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using RegistryKey? key = RegistryReader.OpenSubKey(root, packageId);

            if (key is null)
            {
                continue;
            }

            string? rootFolder = RegistryReader.StringValue(key, "PackageRootFolder");

            // Windows'un kendi sistem uygulamaları kaldırılamıyor; listeyi boğuyorlar.
            if (string.IsNullOrWhiteSpace(rootFolder) ||
                rootFolder.StartsWith(windowsDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Kaynak ve çatı paketleri ayrı program değil, başka paketin parçası.
            if (packageId.Contains("_split.", StringComparison.OrdinalIgnoreCase) ||
                packageId.Contains(".Framework.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            programs.Add(new InstalledProgram
            {
                Id = $"store|{packageId}",
                Name = StoreDisplayName(key, packageId),
                Source = ProgramSource.Store,
                Publisher = RegistryReader.StringValue(key, "PublisherDisplayName") is { Length: > 0 } publisher
                    ? IndirectString.Resolve(publisher)
                    : PublisherFromPackageId(packageId),
                Version = VersionFromPackageId(packageId),
                InstallLocation = rootFolder,
                PackageFullName = packageId,
                IsMachineWide = false
            });
        }
    }

    /// <summary>
    /// Store paketlerinin adı "@{paket?ms-resource://…}" biçiminde kaynak göstergesi.
    /// Çözülemezse paket kimliğinin ilk parçası tek okunabilir metin oluyor.
    /// </summary>
    private static string StoreDisplayName(RegistryKey key, string packageId)
    {
        string? raw = RegistryReader.StringValue(key, "DisplayName");

        if (!string.IsNullOrWhiteSpace(raw))
        {
            string resolved = IndirectString.Resolve(raw);

            if (!resolved.StartsWith('@'))
            {
                return resolved;
            }
        }

        return NameFromPackageId(packageId);
    }

    /// <summary>"Publisher.Uygulama_1.2.3.0_x64__hash" → "Uygulama".</summary>
    public static string NameFromPackageId(string packageId)
    {
        string head = packageId.Split('_')[0];
        int dot = head.LastIndexOf('.');

        return dot > 0 && dot < head.Length - 1 ? head[(dot + 1)..] : head;
    }

    public static string? PublisherFromPackageId(string packageId)
    {
        string head = packageId.Split('_')[0];
        int dot = head.IndexOf('.');

        return dot > 0 ? head[..dot] : null;
    }

    public static string? VersionFromPackageId(string packageId)
    {
        string[] parts = packageId.Split('_');

        return parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
    }
}
