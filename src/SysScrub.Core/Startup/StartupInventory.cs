using System.Diagnostics;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SysScrub.Core.RegistryCleaning;
using SysScrub.Core.Windows;

namespace SysScrub.Core.Startup;

/// <summary>
/// Açılışta çalışan her şeyi tek listede toplar.
///
/// Görev Yöneticisi yalnızca Run kayıtlarını ve Başlangıç klasörünü gösteriyor;
/// oturum açma tetikleyicili zamanlanmış görevler ve otomatik servisler orada yok.
/// Oysa yavaş açılışın sebebi çoğu zaman onlar.
/// </summary>
public sealed class StartupInventory(
    StartupApprovedStore approvals,
    BootPerformance bootPerformance,
    ILogger<StartupInventory>? logger = null)
{
    private static readonly (RegistryHive Hive, string Path, StartupSource Source, StartupApprovedStore.ApprovalScope Scope)[] RegistryLocations =
    [
        (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            StartupSource.RegistryRun, StartupApprovedStore.ApprovalScope.Run),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            StartupSource.RegistryRun, StartupApprovedStore.ApprovalScope.Run),
        (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            StartupSource.RegistryRunOnce, StartupApprovedStore.ApprovalScope.Run),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            StartupSource.RegistryRunOnce, StartupApprovedStore.ApprovalScope.Run)
    ];

    private readonly ILogger _logger = logger ?? NullLogger<StartupInventory>.Instance;

    public async Task<StartupInventoryReport> LoadAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyDictionary<string, int> delays =
            await bootPerformance.LoadDelaysAsync(cancellationToken).ConfigureAwait(false);

        List<StartupEntry> entries = await Task.Run(
            () => Collect(delays, cancellationToken), cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        _logger.LogInformation(
            "Başlangıç envanteri: {Total} öğe, {Enabled} açık, {Elapsed} ms",
            entries.Count, entries.Count(e => e.IsEnabled), stopwatch.ElapsedMilliseconds);

        return new StartupInventoryReport
        {
            Entries = entries
                .OrderByDescending(e => e.IsEnabled)
                .ThenByDescending(e => e.BootDelayMs ?? 0)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            Duration = stopwatch.Elapsed,
            BootMeasurementsAvailable = delays.Count > 0
        };
    }

    private List<StartupEntry> Collect(IReadOnlyDictionary<string, int> delays, CancellationToken cancellationToken)
    {
        var entries = new List<StartupEntry>();

        CollectRegistry(entries, delays, cancellationToken);
        CollectStartupFolders(entries, delays, cancellationToken);
        CollectScheduledTasks(entries, cancellationToken);
        CollectServices(entries, cancellationToken);

        return entries;
    }

    // ------------------------------------------------------------------ Run kayıtları

    private void CollectRegistry(
        List<StartupEntry> entries,
        IReadOnlyDictionary<string, int> delays,
        CancellationToken cancellationToken)
    {
        foreach ((RegistryHive hive, string path, StartupSource source, StartupApprovedStore.ApprovalScope scope)
                 in RegistryLocations)
        {
            foreach (RegistryView view in RegistryReader.ViewsFor(hive, path))
            {
                using RegistryKey? key = RegistryReader.OpenKey(hive, view, path);

                foreach (string valueName in RegistryReader.ValueNames(key))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? command = RegistryReader.StringValue(key, valueName);

                    if (string.IsNullOrWhiteSpace(command))
                    {
                        continue;
                    }

                    // 32-bit görünümdeki kayıtların onay anahtarı Run32.
                    StartupApprovedStore.ApprovalScope actualScope =
                        view == RegistryView.Registry32 && scope == StartupApprovedStore.ApprovalScope.Run
                            ? StartupApprovedStore.ApprovalScope.Run32
                            : scope;

                    string? target = RegistryPathProbe.ExtractPath(command);
                    bool missing = RegistryPathProbe.Probe(command, out _) == RegistryPathProbe.ProbeResult.Missing;

                    entries.Add(new StartupEntry
                    {
                        Id = $"reg|{hive}|{view}|{path}|{valueName}",
                        Name = valueName,
                        Command = command,
                        Source = source,
                        IsEnabled = approvals.IsEnabled(hive, actualScope, valueName),
                        IsMachineWide = hive == RegistryHive.LocalMachine,
                        TargetPath = target,
                        TargetMissing = missing,
                        BootDelayMs = LookupDelay(delays, target),
                        RegistryKeyPath = path,
                        IsMachineHive = hive == RegistryHive.LocalMachine,
                        ApprovalScope = actualScope,
                        ApprovalValueName = valueName
                    });
                }
            }
        }
    }

    // ------------------------------------------------------------------ Başlangıç klasörleri

    private void CollectStartupFolders(
        List<StartupEntry> entries,
        IReadOnlyDictionary<string, int> delays,
        CancellationToken cancellationToken)
    {
        (string Folder, bool Machine)[] folders =
        [
            (Environment.GetFolderPath(Environment.SpecialFolder.Startup), false),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), true)
        ];

        foreach ((string folder, bool machine) in folders)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                continue;
            }

            string[] files;

            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = Path.GetFileName(file);

                // desktop.ini başlangıç öğesi değil, klasör görünüm ayarı.
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entries.Add(new StartupEntry
                {
                    Id = $"folder|{file}",
                    Name = Path.GetFileNameWithoutExtension(file),
                    Command = file,
                    Source = StartupSource.StartupFolder,
                    IsEnabled = approvals.IsEnabled(
                        machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
                        StartupApprovedStore.ApprovalScope.StartupFolder,
                        fileName),
                    IsMachineWide = machine,
                    TargetPath = file,
                    BootDelayMs = LookupDelay(delays, file),
                    ShortcutPath = file,
                    IsMachineHive = machine,
                    ApprovalScope = StartupApprovedStore.ApprovalScope.StartupFolder,
                    ApprovalValueName = fileName
                });
            }
        }
    }

    // ------------------------------------------------------------------ Zamanlanmış görevler

    /// <summary>
    /// Oturum açma tetikleyicili görevler. Görev Yöneticisi bunları başlangıç
    /// sekmesinde göstermiyor ama açılışta çalışıyorlar.
    /// </summary>
    private void CollectScheduledTasks(List<StartupEntry> entries, CancellationToken cancellationToken)
    {
        Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");

        if (serviceType is null)
        {
            return;
        }

        dynamic? service = null;

        try
        {
            service = Activator.CreateInstance(serviceType);

            if (service is null)
            {
                return;
            }

            service.Connect();

            WalkTaskFolder(service.GetFolder("\\"), entries, cancellationToken);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                   or UnauthorizedAccessException
                                   or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            _logger.LogWarning(ex, "Zamanlanmış görevler okunamadı");
        }
        finally
        {
            if (service is not null && System.Runtime.InteropServices.Marshal.IsComObject(service))
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(service);
            }
        }
    }

    private void WalkTaskFolder(dynamic folder, List<StartupEntry> entries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            dynamic tasks = folder.GetTasks(0);

            foreach (dynamic task in tasks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!HasLogonTrigger(task.Definition))
                    {
                        continue;
                    }

                    string path = task.Path;
                    string name = task.Name;

                    // Microsoft'un kendi bakım görevleri listeyi boğuyor ve
                    // kapatılmaları önerilmiyor; kapsam dışında tutuluyorlar.
                    if (path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    entries.Add(new StartupEntry
                    {
                        Id = $"task|{path}",
                        Name = name,
                        Command = FirstActionPath(task.Definition) ?? path,
                        Source = StartupSource.ScheduledTask,
                        IsEnabled = task.Enabled,
                        IsMachineWide = true,
                        TaskPath = path
                    });
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                           or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // Tek bir görev okunamazsa kalanlar listelenmeye devam eder.
                }
            }

            foreach (dynamic child in folder.GetFolders(0))
            {
                WalkTaskFolder(child, entries, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                   or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
        }
    }

    /// <summary>Tetikleyici türü 9 = oturum açma.</summary>
    private static bool HasLogonTrigger(dynamic definition)
    {
        try
        {
            foreach (dynamic trigger in definition.Triggers)
            {
                if ((int)trigger.Type == 9)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                   or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
        }

        return false;
    }

    private static string? FirstActionPath(dynamic definition)
    {
        try
        {
            foreach (dynamic action in definition.Actions)
            {
                // Tür 0 = çalıştırılabilir dosya.
                if ((int)action.Type == 0)
                {
                    return action.Path;
                }
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                   or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
        }

        return null;
    }

    // ------------------------------------------------------------------ Servisler

    /// <summary>
    /// Otomatik başlayan Microsoft dışı servisler. Yalnızca gösteriliyor:
    /// servis başlangıç türünü değiştirmek bambaşka bir risk sınıfı ve
    /// sistem kararlılığını doğrudan etkileyebiliyor.
    /// </summary>
    private void CollectServices(List<StartupEntry> entries, CancellationToken cancellationToken)
    {
        using RegistryKey? services = RegistryReader.OpenKey(
            RegistryHive.LocalMachine, RegistryView.Registry64, @"SYSTEM\CurrentControlSet\Services");

        foreach (string name in RegistryReader.SubKeyNames(services))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using RegistryKey? service = RegistryReader.OpenSubKey(services, name);

            if (service is null)
            {
                continue;
            }

            try
            {
                // Start = 2 → otomatik. Type 16/32 → kendi sürecinde çalışan servis (sürücü değil).
                if (service.GetValue("Start") is not int start || start != 2)
                {
                    continue;
                }

                if (service.GetValue("Type") is not int type || (type & 0x30) == 0)
                {
                    continue;
                }

                string? imagePath = service.GetValue("ImagePath", null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();

                if (string.IsNullOrWhiteSpace(imagePath) || IsWindowsService(imagePath))
                {
                    continue;
                }

                // DisplayName çoğu servis için "@dosya.dll,-245" biçiminde bir kaynak
                // göstergesi; çözülmezse kullanıcı servis adı yerine onu görür.
                string displayName = IndirectString.Resolve(
                    service.GetValue("DisplayName")?.ToString() ?? name);

                entries.Add(new StartupEntry
                {
                    Id = $"service|{name}",
                    Name = displayName.StartsWith('@') ? name : displayName,
                    Command = imagePath,
                    Source = StartupSource.Service,
                    IsEnabled = true,
                    Control = StartupControl.ReadOnly,
                    IsMachineWide = true,
                    TargetPath = RegistryPathProbe.ExtractPath(imagePath)
                });
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
            }
        }
    }

    /// <summary>Windows'un kendi servisleri listeyi boğuyor ve kapatılmaları önerilmiyor.</summary>
    private static bool IsWindowsService(string imagePath) =>
        imagePath.Contains(@"\System32\", StringComparison.OrdinalIgnoreCase) ||
        imagePath.Contains(@"\SysWOW64\", StringComparison.OrdinalIgnoreCase) ||
        imagePath.Contains("svchost", StringComparison.OrdinalIgnoreCase);

    /// <summary>Olay günlüğü dosya adıyla kayıt tutuyor; yolu adına indirgeyip eşleştiriyoruz.</summary>
    private static int? LookupDelay(IReadOnlyDictionary<string, int> delays, string? targetPath)
    {
        if (targetPath is null || delays.Count == 0)
        {
            return null;
        }

        string fileName = Path.GetFileName(targetPath);

        return delays.TryGetValue(fileName, out int delay) ? delay : null;
    }
}
