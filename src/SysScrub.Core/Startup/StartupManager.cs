using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SysScrub.Core.Cleaning;

namespace SysScrub.Core.Startup;

/// <summary>Tek bir açma/kapama işleminin sonucu.</summary>
public sealed record StartupChangeResult
{
    public required bool Success { get; init; }

    /// <summary>Başarısızlıkta kullanıcıya gösterilecek sebep.</summary>
    public string? Message { get; init; }

    public static StartupChangeResult Ok { get; } = new() { Success = true };

    public static StartupChangeResult Fail(string message) => new() { Success = false, Message = message };
}

/// <summary>
/// Başlangıç öğelerini açar/kapatır.
///
/// Hiçbir kayıt silinmez. Registry ve klasör öğelerinde Windows'un kendi
/// <see cref="StartupApprovedStore"/> mekanizması kullanılır: öğe yerinde kalır,
/// yalnızca "onaylı" bayrağı değişir. Bu sayede işlem her zaman geri alınabilir
/// ve Görev Yöneticisi ile aynı durumu gösteririz.
/// </summary>
public sealed class StartupManager(
    StartupApprovedStore approvals,
    HistoryStore history,
    ILogger<StartupManager>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<StartupManager>.Instance;

    public Task<StartupChangeResult> SetEnabledAsync(
        StartupEntry entry,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return Task.Run(() => SetEnabled(entry, enabled), cancellationToken);
    }

    private StartupChangeResult SetEnabled(StartupEntry entry, bool enabled)
    {
        if (entry.Control == StartupControl.ReadOnly)
        {
            return StartupChangeResult.Fail(
                "Servislerin başlangıç türü bu ekrandan değiştirilemez. Yanlış kapatılan bir servis " +
                "sistemin açılışını etkileyebilir; bunun için Hizmetler konsolunu kullanın.");
        }

        StartupChangeResult result = entry.Source switch
        {
            StartupSource.ScheduledTask => SetTaskEnabled(entry, enabled),
            _ => SetApprovalEnabled(entry, enabled)
        };

        if (result.Success)
        {
            Record(entry, enabled);

            _logger.LogInformation(
                "Başlangıç öğesi {State}: {Name} ({Source})",
                enabled ? "açıldı" : "kapatıldı", entry.Name, entry.Source);
        }

        return result;
    }

    private StartupChangeResult SetApprovalEnabled(StartupEntry entry, bool enabled)
    {
        if (entry.ApprovalScope is not { } scope || string.IsNullOrEmpty(entry.ApprovalValueName))
        {
            return StartupChangeResult.Fail("Bu öğenin Windows'taki karşılığı çözülemedi.");
        }

        RegistryHive hive = entry.IsMachineHive ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;

        if (approvals.SetEnabled(hive, scope, entry.ApprovalValueName, enabled))
        {
            return StartupChangeResult.Ok;
        }

        return StartupChangeResult.Fail(
            entry.IsMachineHive
                ? "Tüm kullanıcıları etkileyen öğeyi değiştirmek için yönetici hakkı gerekiyor."
                : "Kayıt yazılamadı; öğe değiştirilemedi.");
    }

    /// <summary>
    /// Zamanlanmış görevler onay anahtarını kullanmaz; görevin kendi
    /// <c>Enabled</c> özelliği değiştirilir. Görev tanımı bozulmaz.
    /// </summary>
    private StartupChangeResult SetTaskEnabled(StartupEntry entry, bool enabled)
    {
        if (string.IsNullOrEmpty(entry.TaskPath))
        {
            return StartupChangeResult.Fail("Görev yolu bilinmiyor.");
        }

        Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");

        if (serviceType is null)
        {
            return StartupChangeResult.Fail("Görev Zamanlayıcı hizmetine erişilemedi.");
        }

        dynamic? service = null;

        try
        {
            service = Activator.CreateInstance(serviceType);

            if (service is null)
            {
                return StartupChangeResult.Fail("Görev Zamanlayıcı hizmetine erişilemedi.");
            }

            service.Connect();

            string folderPath = FolderOf(entry.TaskPath);
            dynamic folder = service.GetFolder(folderPath);
            dynamic task = folder.GetTask(NameOf(entry.TaskPath));

            task.Enabled = enabled;

            return StartupChangeResult.Ok;
        }
        catch (UnauthorizedAccessException)
        {
            return StartupChangeResult.Fail("Bu görevi değiştirmek için yönetici hakkı gerekiyor.");
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            _logger.LogWarning(ex, "Zamanlanmış görev değiştirilemedi: {Task}", entry.TaskPath);

            return StartupChangeResult.Fail($"Görev değiştirilemedi: {ex.Message}");
        }
        finally
        {
            if (service is not null && Marshal.IsComObject(service))
            {
                Marshal.FinalReleaseComObject(service);
            }
        }
    }

    /// <summary>Değişiklik zaman tüneline yazılır; kullanıcı ne zaman neyi kapattığını görebilir.</summary>
    private void Record(StartupEntry entry, bool enabled)
    {
        history.Append(
            new HistoryRun
            {
                RunId = Guid.NewGuid(),
                Operation = HistoryOperation.StartupChange,
                StartedAt = DateTimeOffset.Now,
                Duration = TimeSpan.Zero,
                BytesFreed = 0,
                ItemsAffected = 1,
                // Aynı düğme tersine basıldığında geri alınıyor; ayrı bir geri alma akışı gerekmiyor.
                IsReversible = false,
                RuleIds = [$"startup:{entry.Source}:{entry.Name}"]
            },
            [
                new HistoryItem
                {
                    Path = entry.Command,
                    RuleId = enabled ? "startup.enable" : "startup.disable",
                    Bytes = 0,
                    Outcome = HistoryItemOutcome.Changed,
                    Message = $"{entry.Name} — {(enabled ? "açıldı" : "devre dışı bırakıldı")} ({entry.SourceLabel})"
                }
            ]);
    }

    private static string FolderOf(string taskPath)
    {
        int slash = taskPath.LastIndexOf('\\');

        return slash <= 0 ? "\\" : taskPath[..slash];
    }

    private static string NameOf(string taskPath)
    {
        int slash = taskPath.LastIndexOf('\\');

        return slash < 0 ? taskPath : taskPath[(slash + 1)..];
    }
}
