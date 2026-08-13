using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SysScrub.Core.Machine;

/// <summary>Zamanlanmış görevin durumu.</summary>
public sealed record MaintenanceTaskState
{
    public required bool Exists { get; init; }

    public bool IsEnabled { get; init; }

    /// <summary>Görevin çalışacağı saat; okunamazsa null.</summary>
    public int? Hour { get; init; }

    public DateTime? NextRun { get; init; }

    public string? Message { get; init; }

    public static MaintenanceTaskState Missing { get; } = new() { Exists = false };
}

/// <summary>
/// Haftalık otomatik temizlik görevi.
///
/// Neden Run kaydı değil de Zamanlanmış Görev: temizlik yönetici hakkı istiyor.
/// Run kaydıyla açılışta çalıştırmak her seferinde UAC penceresi demek olurdu.
/// "En yüksek ayrıcalıklarla" işaretli bir görev, kullanıcıyı rahatsız etmeden
/// yetkiyle çalışıyor.
///
/// Görev komut satırı sürümünü çağırıyor, arayüzü değil: kullanıcı bilgisayarda
/// olmasa da penceresiz çalışması gerekiyor.
/// </summary>
public sealed class ScheduledMaintenance(ILogger<ScheduledMaintenance>? logger = null)
{
    public const string TaskName = "SysScrub Haftalık Bakım";

    /// <summary>Yalnızca varsayılan açık, güvenli işaretli kurallar çalışır.</summary>
    private const string Arguments = "clean --apply --yes";

    private const string CliFileName = "sysscrub-cli.exe";

    // Görev Zamanlayıcı sabitleri.
    private const int TriggerWeekly = 3;
    private const int ActionExecute = 0;
    private const int CreateOrUpdate = 6;
    private const int LogonInteractiveToken = 3;
    private const int RunLevelHighest = 1;
    private const short Sunday = 0x01;

    private readonly ILogger _logger = logger ?? NullLogger<ScheduledMaintenance>.Instance;

    /// <summary>Komut satırı sürümünün yolu; kurulu değilse null.</summary>
    public static string? CliPath
    {
        get
        {
            string path = System.IO.Path.Combine(AppPaths.InstallDirectory, CliFileName);

            return File.Exists(path) ? path : null;
        }
    }

    public MaintenanceTaskState Query()
    {
        dynamic? service = null;

        try
        {
            service = Connect();

            if (service is null)
            {
                return MaintenanceTaskState.Missing;
            }

            dynamic folder = service.GetFolder("\\");
            dynamic task = folder.GetTask(TaskName);

            return new MaintenanceTaskState
            {
                Exists = true,
                IsEnabled = task.Enabled,
                Hour = HourOf(task.Definition),
                NextRun = NextRunOf(task)
            };
        }
        catch (Exception ex) when (IsTaskMissing(ex))
        {
            // Görev yoksa bu bir arıza değil, normal durum.
            return MaintenanceTaskState.Missing;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                   or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return MaintenanceTaskState.Missing with { Message = ex.Message };
        }
        finally
        {
            Release(service);
        }
    }

    /// <summary>
    /// Görev bulunamadı mı.
    ///
    /// Görev Zamanlayıcı "bulunamadı" durumunu 0x80070002 ile bildiriyor ve dinamik
    /// COM bağlayıcısı bunu <see cref="FileNotFoundException"/>'a çeviriyor —
    /// <see cref="COMException"/>'a değil. Yalnızca COMException yakalamak, görev
    /// kurulu olmayan her makinede Ayarlar ekranını çökertirdi.
    /// </summary>
    private static bool IsTaskMissing(Exception exception) =>
        exception is COMException or FileNotFoundException or DirectoryNotFoundException;

    /// <summary>Görevi oluşturur ya da günceller.</summary>
    public MaintenanceTaskState Register(int hour)
    {
        if (CliPath is not { Length: > 0 } cli)
        {
            return MaintenanceTaskState.Missing with
            {
                Message = $"{CliFileName} bulunamadı; zamanlanmış görev bu kuruluma eklenemiyor."
            };
        }

        dynamic? service = null;

        try
        {
            service = Connect();

            if (service is null)
            {
                return MaintenanceTaskState.Missing with { Message = "Görev Zamanlayıcı hizmetine erişilemedi." };
            }

            dynamic folder = service.GetFolder("\\");
            dynamic definition = service.NewTask(0);

            definition.RegistrationInfo.Description =
                "SysScrub haftalık otomatik temizlik. Yalnızca güvenli işaretli kurallar çalışır; " +
                "her silme karantinaya alınır ve geri alınabilir.";
            definition.RegistrationInfo.Author = "SysScrub";

            definition.Principal.RunLevel = RunLevelHighest;

            definition.Settings.StartWhenAvailable = true;
            definition.Settings.DisallowStartIfOnBatteries = true;
            definition.Settings.StopIfGoingOnBatteries = true;
            definition.Settings.ExecutionTimeLimit = "PT2H";

            dynamic trigger = definition.Triggers.Create(TriggerWeekly);
            trigger.StartBoundary = StartBoundary(hour);
            trigger.DaysOfWeek = Sunday;
            trigger.WeeksInterval = 1;

            dynamic action = definition.Actions.Create(ActionExecute);
            action.Path = cli;
            action.Arguments = Arguments;
            action.WorkingDirectory = AppPaths.InstallDirectory;

            folder.RegisterTaskDefinition(
                TaskName, definition, CreateOrUpdate, null, null, LogonInteractiveToken, null);

            _logger.LogInformation("Zamanlanmış bakım görevi kaydedildi: her pazar saat {Hour}:00", hour);

            return Query();
        }
        catch (UnauthorizedAccessException)
        {
            return MaintenanceTaskState.Missing with
            {
                Message = "Görev oluşturmak için yönetici hakkı gerekiyor."
            };
        }
        catch (Exception ex) when (ex is COMException
                                   or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            _logger.LogWarning(ex, "Zamanlanmış görev kaydedilemedi");

            return MaintenanceTaskState.Missing with { Message = $"Görev oluşturulamadı: {ex.Message}" };
        }
        finally
        {
            Release(service);
        }
    }

    public MaintenanceTaskState Remove()
    {
        dynamic? service = null;

        try
        {
            service = Connect();

            if (service is null)
            {
                return MaintenanceTaskState.Missing;
            }

            service.GetFolder("\\").DeleteTask(TaskName, 0);

            _logger.LogInformation("Zamanlanmış bakım görevi kaldırıldı");

            return MaintenanceTaskState.Missing;
        }
        catch (Exception ex) when (IsTaskMissing(ex))
        {
            // Zaten yoksa istenen durumdayız.
            return MaintenanceTaskState.Missing;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                   or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return MaintenanceTaskState.Missing with { Message = ex.Message };
        }
        finally
        {
            Release(service);
        }
    }

    /// <summary>Tetikleyici başlangıcı yerel saatte ISO 8601; Görev Zamanlayıcı bu biçimi bekliyor.</summary>
    public static string StartBoundary(int hour)
    {
        DateTime start = DateTime.Today.AddHours(Math.Clamp(hour, 0, 23));

        return start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static dynamic? Connect()
    {
        Type? type = Type.GetTypeFromProgID("Schedule.Service");

        if (type is null)
        {
            return null;
        }

        dynamic? service = Activator.CreateInstance(type);
        service?.Connect();

        return service;
    }

    private static int? HourOf(dynamic definition)
    {
        try
        {
            foreach (dynamic trigger in definition.Triggers)
            {
                string boundary = trigger.StartBoundary;

                if (DateTime.TryParse(
                        boundary, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                {
                    return parsed.Hour;
                }
            }
        }
        catch (Exception ex) when (ex is COMException
                                   or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
        }

        return null;
    }

    private static DateTime? NextRunOf(dynamic task)
    {
        try
        {
            DateTime next = task.NextRunTime;

            return next > DateTime.MinValue ? next : null;
        }
        catch (Exception ex) when (ex is COMException
                                   or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
    }

    private static void Release(dynamic? service)
    {
        if (service is not null && Marshal.IsComObject(service))
        {
            Marshal.FinalReleaseComObject(service);
        }
    }
}
