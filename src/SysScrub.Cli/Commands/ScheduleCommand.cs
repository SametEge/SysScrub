using System.Globalization;
using SysScrub.Core.Machine;
using SysScrub.Core.Settings;

namespace SysScrub.Cli.Commands;

/// <summary>
/// Haftalık otomatik bakım görevi. Arayüzdeki anahtarın komut satırı karşılığı;
/// kurulum betiklerinden ve uzaktan yönetimden kullanılabilsin diye var.
/// </summary>
internal static class ScheduleCommand
{
    public static int Run(string[] args)
    {
        var maintenance = new ScheduledMaintenance();

        bool on = args.Contains("--on", StringComparer.OrdinalIgnoreCase);
        bool off = args.Contains("--off", StringComparer.OrdinalIgnoreCase);

        if (on && off)
        {
            Console.Error.WriteLine("--on ve --off birlikte kullanılamaz.");
            return 1;
        }

        if (off)
        {
            MaintenanceTaskState removed = maintenance.Remove();

            Console.WriteLine(removed.Message ?? "Haftalık bakım görevi kaldırıldı.");
            return removed.Message is null ? 0 : 1;
        }

        if (on)
        {
            int hour = HourFrom(args);

            MaintenanceTaskState created = maintenance.Register(hour);

            if (!created.Exists)
            {
                Console.Error.WriteLine(created.Message ?? "Görev oluşturulamadı.");
                return 1;
            }

            // Ayar dosyası da güncelleniyor ki arayüz aynı durumu göstersin.
            new SettingsStore().Update(s => s with { ScheduledCleanup = true, ScheduledHour = hour });

            Console.WriteLine($"Haftalık bakım görevi kuruldu: her pazar saat {hour:00}:00.");
            Print(created);

            return 0;
        }

        Print(maintenance.Query());
        return 0;
    }

    private static void Print(MaintenanceTaskState state)
    {
        if (!state.Exists)
        {
            Console.WriteLine("Haftalık bakım görevi kurulu değil.");

            if (state.Message is { Length: > 0 } message)
            {
                Console.WriteLine(message);
            }

            return;
        }

        Console.WriteLine($"Görev: {ScheduledMaintenance.TaskName}");
        Console.WriteLine($"  Durum          {(state.IsEnabled ? "açık" : "kapalı")}");

        if (state.Hour is { } hour)
        {
            Console.WriteLine($"  Çalışma saati  her pazar {hour:00}:00");
        }

        if (state.NextRun is { } next)
        {
            Console.WriteLine($"  Sıradaki       {next.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture)}");
        }
    }

    private static int HourFrom(string[] args)
    {
        int index = Array.FindIndex(args, a => a.Equals("--hour", StringComparison.OrdinalIgnoreCase));

        if (index >= 0 && index + 1 < args.Length &&
            int.TryParse(args[index + 1], out int hour))
        {
            return Math.Clamp(hour, 0, 23);
        }

        return AppSettings.DefaultScheduledHour;
    }
}
