using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SysScrub.App.Services;
using SysScrub.App.ViewModels;
using SysScrub.App.Views;
using SysScrub.Core.Cleaning;
using SysScrub.Core.Drivers;
using SysScrub.Core.Machine;
using SysScrub.Core.RegistryCleaning;
using SysScrub.Core.Rules;
using SysScrub.Core.Safety;
using SysScrub.Core.Software;
using SysScrub.Core.Startup;

namespace SysScrub.App;

public partial class App : Application
{
    private IHost? _host;

    /// <summary>
    /// DataTemplate içinden oluşturulan sayfalar bağımlılıklarını buradan çözer.
    /// Görünüm ağacının her yerine servis sağlayıcı taşımak yerine tek erişim noktası.
    /// </summary>
    public static T Resolve<T>() where T : notnull =>
        ((App)Current)._host!.Services.GetRequiredService<T>();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string dataRoot = AppPaths.DataDirectory;
        Directory.CreateDirectory(dataRoot);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(dataRoot, "logs", "sysscrub-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton<SystemInfoService>();
                services.AddSingleton<ThemeService>();
                services.AddSingleton<ElevationService>();

                // Temizlik zinciri: kural kümesi bir kez yüklenir, motorlar onu paylaşır.
                services.AddSingleton<PathResolver>();
                services.AddSingleton<SafetyGuard>();
                services.AddSingleton(_ => new RuleLoader().Load());
                services.AddSingleton<QuarantineStore>();
                services.AddSingleton<HistoryStore>();
                services.AddSingleton<ScanEngine>();
                services.AddSingleton<CleanEngine>();

                // Yazılım güncelleyici
                services.AddSingleton<WingetService>();

                // Sürücü zinciri
                services.AddSingleton<DeviceInventory>();
                services.AddSingleton<DriverBackup>();
                services.AddSingleton<WindowsUpdateDriverSource>();

                // Başlangıç zinciri
                services.AddSingleton<StartupApprovedStore>();
                services.AddSingleton<BootPerformance>();
                services.AddSingleton<StartupInventory>();
                services.AddSingleton<StartupManager>();

                // Registry zinciri
                services.AddSingleton<RegistryGuard>();
                services.AddSingleton<SystemRestorePoint>();
                services.AddSingleton<RegistryScanEngine>();
                services.AddSingleton<RegistryCleanEngine>();

                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<CleanerViewModel>();
                services.AddSingleton<RegistryViewModel>();
                services.AddSingleton<DriversViewModel>();
                services.AddSingleton<SoftwareUpdatesViewModel>();
                services.AddSingleton<StartupViewModel>();
                services.AddSingleton<TimelineViewModel>();
                services.AddTransient<DashboardViewModel>();

                services.AddSingleton<MainWindow>();
            })
            .Build();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        Resolve<ThemeService>().Initialize();

        Log.Information("SysScrub başlatıldı, sürüm {Version}", GetType().Assembly.GetName().Version);

        MainWindow window = Resolve<MainWindow>();

        if (WindowCapture.PageFromArgs(e.Args) is { } pageTitle)
        {
            MainWindowViewModel viewModel = Resolve<MainWindowViewModel>();
            viewModel.SelectedItem = viewModel.Items
                .FirstOrDefault(i => i.Title.Equals(pageTitle, StringComparison.OrdinalIgnoreCase))
                ?? viewModel.SelectedItem;
        }

        window.Show();

        if (WindowCapture.PathFromArgs(e.Args) is { } screenshotPath)
        {
            CaptureAndExit(
                window,
                screenshotPath,
                e.Args.Contains("--autoscan") || e.Args.Contains("--busyshot"),
                e.Args.Contains("--busyshot"),
                e.Args.Contains("--regscan"),
                e.Args.Contains("--devscan"),
                e.Args.Contains("--wingetscan"),
                e.Args.Contains("--startupscan"));
        }
    }

    /// <summary>
    /// Geliştirme anahtarı: pencere çizildikten sonra ekran görüntüsünü alıp çıkar.
    /// --autoscan verilirse önce tarama tamamlanır, böylece görüntü dolu ekranı gösterir.
    /// </summary>
    private void CaptureAndExit(
        Window window,
        string screenshotPath,
        bool autoScan,
        bool captureWhileBusy = false,
        bool registryScan = false,
        bool deviceScan = false,
        bool wingetScan = false,
        bool startupScan = false)
    {
        window.ContentRendered += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            async () =>
            {
                if (startupScan)
                {
                    await Resolve<StartupViewModel>().LoadCommand.ExecuteAsync(null);

                    await Task.Delay(250);
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                }


                if (wingetScan)
                {
                    await Resolve<SoftwareUpdatesViewModel>().CheckCommand.ExecuteAsync(null);

                    await Task.Delay(250);
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                }

                if (deviceScan)
                {
                    DriversViewModel drivers = Resolve<DriversViewModel>();
                    await drivers.LoadCommand.ExecuteAsync(null);

                    await Task.Delay(250);
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                }

                if (registryScan)
                {
                    RegistryViewModel registry = Resolve<RegistryViewModel>();
                    registry.SelectAllCommand.Execute(null);
                    await registry.ScanCommand.ExecuteAsync(null);

                    foreach (RegistryScannerNodeViewModel node in registry.Scanners.Where(n => n.HasFindings).Take(2))
                    {
                        node.IsExpanded = true;
                    }

                    await Task.Delay(250);
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                }

                if (autoScan)
                {
                    CleanerViewModel cleaner = Resolve<CleanerViewModel>();

                    if (captureWhileBusy)
                    {
                        // Taramayı bekletmeden başlat ki örtü ekranı iş üstünde yakalansın.
                        cleaner.SelectAllCommand.Execute(null);
                        _ = cleaner.ScanCommand.ExecuteAsync(null);
                        await Task.Delay(60);
                    }
                    else
                    {
                        await cleaner.ScanCommand.ExecuteAsync(null);

                        // Komut durumları yeniden sorgulanmadan görüntü alınırsa butonlar
                        // devre dışıymış gibi çıkıyor; yerleşmesini bekliyoruz.
                        await Task.Delay(250);
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    }
                }

                WindowCapture.Save(window, screenshotPath);
                Log.Information("Ekran görüntüsü yazıldı: {Path}", screenshotPath);
                Shutdown();
            });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("SysScrub kapatılıyor");

        if (_host is not null)
        {
            Resolve<ThemeService>().Shutdown();
            _host.Dispose();
        }

        Log.CloseAndFlush();

        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Arayüzde yakalanmamış hata");

        MessageBox.Show(
            $"Beklenmeyen bir hata oluştu.\n\n{e.Exception.Message}\n\nAyrıntılar günlük dosyasında:\n{AppPaths.DataDirectory}",
            "SysScrub",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Uygulamayı ayakta tutuyoruz: tek bir sayfa hatası tüm oturumu düşürmemeli.
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.ExceptionObject as Exception, "Uygulama alanında yakalanmamış hata");
        Log.CloseAndFlush();
    }
}
