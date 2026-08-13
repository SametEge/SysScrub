using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SysScrub.Core.Cleaning;
using SysScrub.Core.RegistryCleaning;
using SysScrub.Core.Safety;
using SysScrub.Core.Windows;

namespace SysScrub.Core.Programs;

/// <summary>Kaldırma isteğinin sonucu.</summary>
public enum UninstallOutcome
{
    /// <summary>Program gitti; kaydı artık yok.</summary>
    Removed,

    /// <summary>Kaldırıcı bitti ama kayıt duruyor — iptal edilmiş ya da arka planda sürüyor olabilir.</summary>
    StillPresent,

    /// <summary>Kaldırıcı çalıştırılamadı.</summary>
    Failed,

    /// <summary>Bu programın kaldırma komutu yok.</summary>
    NoUninstaller,

    /// <summary>Kullanıcı beklemeyi iptal etti.</summary>
    Cancelled
}

public sealed record UninstallResult
{
    public required string ProgramId { get; init; }

    public required string ProgramName { get; init; }

    public required UninstallOutcome Outcome { get; init; }

    public string? Message { get; init; }

    /// <summary>Kaldırma sonrası yerinde kalan kurulum klasörü ve boyutu.</summary>
    public string? LeftoverDirectory { get; init; }

    public long LeftoverBytes { get; init; }

    public bool Succeeded => Outcome == UninstallOutcome.Removed;

    public bool HasLeftover => LeftoverDirectory is not null;

    public string Describe() => Outcome switch
    {
        UninstallOutcome.Removed => $"{ProgramName} kaldırıldı.",
        UninstallOutcome.StillPresent =>
            $"{ProgramName} hâlâ kayıtlı. Kaldırma iptal edilmiş ya da arka planda sürüyor olabilir.",
        UninstallOutcome.NoUninstaller => $"{ProgramName} için kaldırma komutu tanımlı değil.",
        UninstallOutcome.Cancelled => $"{ProgramName} kaldırması beklenmedi; işlem arka planda sürüyor olabilir.",
        _ => Message is null ? $"{ProgramName} kaldırılamadı." : $"{ProgramName} kaldırılamadı: {Message}"
    };
}

/// <summary>
/// Programları kaldırır.
///
/// Kaldırma işini programın kendi kaldırıcısı yapıyor — biz onun yerine dosya
/// silmiyoruz. Yaptığımız üç şey var: komutu doğru ayrıştırmak, süreç ağacının
/// tamamının bitmesini beklemek ve sonucu kaydın gerçekten silinip silinmediğine
/// bakarak doğrulamak.
///
/// Çıkış kodu tek başına yeterli değil: kaldırıcıların bir kısmı işi alt sürece
/// devredip sıfır dönüyor, bir kısmı iptal edildiğinde de sıfır dönüyor. Tek
/// güvenilir kanıt kaydın kaybolması.
/// </summary>
public sealed class ProgramUninstaller(
    SafetyGuard guard,
    HistoryStore history,
    ILogger<ProgramUninstaller>? logger = null)
{
    /// <summary>Tek bir kaldırma için üst sınır. Kullanıcı sihirbazı açık unutabiliyor.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    private readonly ILogger _logger = logger ?? NullLogger<ProgramUninstaller>.Instance;

    public async Task<UninstallResult> UninstallAsync(
        InstalledProgram program,
        bool preferQuiet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);

        UninstallCommand? command = BuildCommand(program, preferQuiet);

        if (command is not { IsValid: true } launch)
        {
            return new UninstallResult
            {
                ProgramId = program.Id,
                ProgramName = program.Name,
                Outcome = UninstallOutcome.NoUninstaller
            };
        }

        _logger.LogInformation(
            "Kaldırılıyor: {Name} → {File} {Arguments}", program.Name, launch.FileName, launch.Arguments);

        ProcessTreeResult run = await ProcessTree
            .RunAndWaitAsync(launch.FileName, launch.Arguments, Timeout, cancellationToken)
            .ConfigureAwait(false);

        if (run.Outcome == ProcessTreeOutcome.NotStarted)
        {
            return Record(program, new UninstallResult
            {
                ProgramId = program.Id,
                ProgramName = program.Name,
                Outcome = UninstallOutcome.Failed,
                Message = run.Message
            });
        }

        if (run.Outcome == ProcessTreeOutcome.Cancelled)
        {
            return new UninstallResult
            {
                ProgramId = program.Id,
                ProgramName = program.Name,
                Outcome = UninstallOutcome.Cancelled
            };
        }

        bool removed = !StillRegistered(program);

        (string? leftover, long leftoverBytes) = removed ? FindLeftover(program) : (null, 0L);

        return Record(program, new UninstallResult
        {
            ProgramId = program.Id,
            ProgramName = program.Name,
            Outcome = removed ? UninstallOutcome.Removed : UninstallOutcome.StillPresent,
            LeftoverDirectory = leftover,
            LeftoverBytes = leftoverBytes
        });
    }

    /// <summary>
    /// Store paketleri için PowerShell'in paket komutu kullanılıyor; registry
    /// programları kendi kaldırıcılarını çalıştırıyor.
    /// </summary>
    private static UninstallCommand? BuildCommand(InstalledProgram program, bool preferQuiet)
    {
        if (program.Source == ProgramSource.Store)
        {
            return program.PackageFullName is { Length: > 0 } package
                ? new UninstallCommand(
                    "powershell.exe",
                    "-NoProfile -NonInteractive -Command " +
                    $"\"Remove-AppxPackage -Package '{package}' -ErrorAction Stop\"")
                : null;
        }

        if (preferQuiet)
        {
            // Yayıncının kendi sessiz komutu varsa her zaman o tercih edilir:
            // hangi anahtarların güvenli olduğunu en iyi o biliyor.
            if (!string.IsNullOrWhiteSpace(program.QuietUninstallCommand))
            {
                return UninstallCommandLine.Parse(program.QuietUninstallCommand);
            }

            if (UninstallCommandLine.ToSilentMsi(program.UninstallCommand) is { } silent)
            {
                return silent;
            }
        }

        return string.IsNullOrWhiteSpace(program.UninstallCommand)
            ? null
            : UninstallCommandLine.Parse(program.UninstallCommand);
    }

    /// <summary>Kaydın hâlâ durup durmadığı — kaldırmanın tek güvenilir kanıtı.</summary>
    private static bool StillRegistered(InstalledProgram program)
    {
        if (program.Source == ProgramSource.Store)
        {
            return program.InstallLocation is { Length: > 0 } location && Directory.Exists(location);
        }

        if (program.RegistryKeyPath is not { Length: > 0 } keyPath)
        {
            return false;
        }

        using RegistryKey? key = RegistryReader.OpenKey(program.Hive, program.View, keyPath);

        // Kaldırıcıların bir kısmı anahtarı bırakıp yalnızca DisplayName'i siliyor.
        return key is not null && RegistryReader.StringValue(key, "DisplayName") is { Length: > 0 };
    }

    /// <summary>
    /// Kaldırma sonrası yerinde kalan kurulum klasörü.
    ///
    /// Güvenlik denetiminden geçmeyen bir yol hiç bildirilmiyor: kullanıcıya
    /// silemeyeceğimiz bir şeyi önermek işe yaramaz.
    /// </summary>
    private (string? Directory, long Bytes) FindLeftover(InstalledProgram program)
    {
        if (!program.HasScannableLocation)
        {
            return (null, 0);
        }

        string location = program.InstallLocation!;

        GuardVerdict verdict = guard.InspectDirectory(location, location);

        if (!verdict.IsAllowed)
        {
            _logger.LogInformation(
                "Artık klasör güvenlik denetimini geçmedi, bildirilmiyor: {Path} ({Reason})",
                location, verdict.Reason);

            return (null, 0);
        }

        try
        {
            long bytes = new ProgramSizeCalculator().Measure(location);

            return bytes > 0 ? (location, bytes) : (null, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return (null, 0);
        }
    }

    /// <summary>
    /// Artık klasörü Geri Dönüşüm Kutusu'na taşır.
    ///
    /// Kalıcı silme değil, kutuya gönderme: yanlış klasör silindiyse kullanıcı
    /// tek tıkla geri alabilsin. Denetim burada yeniden yapılıyor — tarama ile
    /// silme arasında yol değişmiş olabilir.
    /// </summary>
    public Task<bool> RemoveLeftoverAsync(string directory, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (!Directory.Exists(directory) || !guard.InspectDirectory(directory, directory).IsAllowed)
            {
                return false;
            }

            long bytes = new ProgramSizeCalculator().Measure(directory, cancellationToken);
            bool removed = ShellFileOperations.DeleteToRecycleBin([directory]);

            if (removed)
            {
                _logger.LogInformation("Artık klasör Geri Dönüşüm Kutusu'na taşındı: {Path}", directory);

                history.Append(
                    new HistoryRun
                    {
                        RunId = Guid.NewGuid(),
                        Operation = HistoryOperation.Uninstall,
                        StartedAt = DateTimeOffset.Now,
                        Duration = TimeSpan.Zero,
                        BytesFreed = bytes,
                        ItemsAffected = 1,
                        // Geri Dönüşüm Kutusu'nda duruyor; kullanıcı oradan geri alabilir.
                        IsReversible = true,
                        RuleIds = ["programs.leftover"]
                    },
                    [
                        new HistoryItem
                        {
                            Path = directory,
                            RuleId = "programs.leftover",
                            Bytes = bytes,
                            Outcome = HistoryItemOutcome.RecycleBin,
                            Message = "Kaldırma sonrası kalan kurulum klasörü"
                        }
                    ]);
            }

            return removed;
        }, cancellationToken);

    private UninstallResult Record(InstalledProgram program, UninstallResult result)
    {
        // Başarısız denemeler de yazılıyor: "ben bunu kaldırmıştım" sorusunun
        // cevabı yalnızca başarılı işlemlerde değil.
        history.Append(
            new HistoryRun
            {
                RunId = Guid.NewGuid(),
                Operation = HistoryOperation.Uninstall,
                StartedAt = DateTimeOffset.Now,
                Duration = TimeSpan.Zero,
                BytesFreed = result.Succeeded ? program.SizeBytes : 0,
                ItemsAffected = result.Succeeded ? 1 : 0,
                ItemsFailed = result.Succeeded ? 0 : 1,
                IsReversible = false,
                RuleIds = [$"programs:{program.Name}"]
            },
            [
                new HistoryItem
                {
                    Path = program.InstallLocation ?? program.Name,
                    RuleId = "programs.uninstall",
                    Bytes = result.Succeeded ? program.SizeBytes : 0,
                    Outcome = result.Succeeded ? HistoryItemOutcome.Changed : HistoryItemOutcome.Failed,
                    Message = result.Describe()
                }
            ]);

        return result;
    }
}
