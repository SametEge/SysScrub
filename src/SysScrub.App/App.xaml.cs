using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SysScrub.App.Localization;
using SysScrub.App.Services;
using SysScrub.App.ViewModels;
using SysScrub.App.Views;
using SysScrub.Core.Analysis;
using SysScrub.Core.Cleaning;
using SysScrub.Core.Disks;
using SysScrub.Core.Drivers;
using SysScrub.Core.Machine;
using SysScrub.Core.Programs;
using SysScrub.Core.RegistryCleaning;
using SysScrub.Core.Rules;
using SysScrub.Core.Safety;
using SysScrub.Core.Settings;
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

                // Ayarlar
                services.AddSingleton<SettingsStore>();
                services.AddSingleton<ScheduledMaintenance>();

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

                // Disk sağlığı
                services.AddSingleton<SmartAttributeTable>();
                services.AddSingleton<DiskInventory>();

                // Disk analizi
                services.AddSingleton<DiskScanner>();
                services.AddSingleton<DuplicateFinder>();

                // Program kaldırıcı
                services.AddSingleton<ProgramInventory>();
                services.AddSingleton<ProgramSizeCalculator>();
                services.AddSingleton<ProgramUninstaller>();

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
                services.AddSingleton<ProgramsViewModel>();
                services.AddSingleton<DiskHealthViewModel>();
                services.AddSingleton<DiskAnalysisViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<TimelineViewModel>();
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<WelcomeViewModel>();

                services.AddSingleton(_ => LocalizationService.Instance);
                services.AddSingleton<MainWindow>();
                services.AddTransient<WelcomeWindow>();
            })
            .Build();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        AppSettings settings = Resolve<SettingsStore>().Current;

        // Dil ilk çizimden önce yerleşmeli; sonra ayarlansa arayüz bir dilde
        // çizilip hemen başka dile dönerdi. --lang geçici geçersiz kılma:
        // ayar dosyasına dokunmuyor.
        LocalizationService.Instance.Use(WindowCapture.LanguageFromArgs(e.Args) ?? settings.Language);

        // Kaydedilmiş tema tercihini uygula, sonra başlat: ilk çizimden önce
        // doğru palet yerinde olsun, açılışta tema atlaması olmasın.
        ThemeService theme = Resolve<ThemeService>();
        theme.Mode = SettingsViewModel.ToThemeMode(settings.Theme);
        theme.Initialize();

        Log.Information(
            "SysScrub başlatıldı, sürüm {Version}, dil {Culture}",
            GetType().Assembly.GetName().Version,
            LocalizationService.Instance.Culture);

        // Ekran görüntüsü alırken tur araya girmemeli.
        bool automated = WindowCapture.PathFromArgs(e.Args) is not null;

        // Geliştirme anahtarı: turun kendisinin ekran görüntüsünü al.
        if (automated && e.Args.Contains("--tourshot"))
        {
            CaptureWelcomeAndExit(WindowCapture.PathFromArgs(e.Args)!, e.Args);
            return;
        }

        if (!settings.TourCompleted && !automated)
        {
            ShowWelcome();
        }

        MainWindow window = Resolve<MainWindow>();

        if (WindowCapture.PageFromArgs(e.Args) is { } pageTitle)
        {
            MainWindowViewModel viewModel = Resolve<MainWindowViewModel>();

            // Ayarlar alt listede duruyor; yalnızca ana listeye bakmak onu bulamıyordu.
            if (viewModel.Items.FirstOrDefault(Matches) is { } item)
            {
                viewModel.SelectedItem = item;
            }
            else if (viewModel.FooterItems.FirstOrDefault(Matches) is { } footer)
            {
                viewModel.SelectedFooterItem = footer;
            }

            bool Matches(NavigationItem candidate) =>
                candidate.Id.Equals(pageTitle, StringComparison.OrdinalIgnoreCase);
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
                e.Args.Contains("--startupscan"),
                e.Args.Contains("--programscan"),
                e.Args.Contains("--diskscan"),
                e.Args.Contains("--analyzescan"));
        }
    }

    /// <summary>
    /// Karşılama turunu gösterir. Ana pencereden önce açılıyor: kullanıcı dilini
    /// seçmeden arayüzü görmesin.
    /// </summary>
    public static void ShowWelcome()
    {
        WelcomeWindow welcome = Resolve<WelcomeWindow>();

        welcome.ShowDialog();

        // Pencere çarpıyla kapatıldıysa tur tamamlanmış sayılmıyor ve bir sonraki
        // açılışta tekrar çıkıyor: dil seçimi atlanmış olabilir.
        Log.Information("Karşılama turu {Result}", welcome.Completed ? "tamamlandı" : "kapatıldı");
    }

    /// <summary>
    /// Geliştirme anahtarı: karşılama turunun belirtilen adımını yakalar.
    /// <c>--tourstep=2</c> ile adım seçilir.
    /// </summary>
    private void CaptureWelcomeAndExit(string path, IReadOnlyList<string> args)
    {
        WelcomeWindow welcome = Resolve<WelcomeWindow>();

        int step = 0;

        foreach (string arg in args)
        {
            if (arg.StartsWith("--tourstep=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(arg["--tourstep=".Length..], out int parsed))
            {
                step = Math.Clamp(parsed, 0, WelcomeViewModel.StepCount - 1);
            }
        }

        if (welcome.DataContext is WelcomeViewModel viewModel)
        {
            viewModel.Step = step;
        }

        welcome.ContentRendered += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            async () =>
            {
                await Task.Delay(250);

                WindowCapture.Save(welcome, path);
                Log.Information("Tur ekran görüntüsü yazıldı: {Path}", path);

                Shutdown();
            });

        welcome.Show();
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
        bool startupScan = false,
        bool programScan = false,
        bool diskScan = false,
        bool analyzeScan = false)
    {
        window.ContentRendered += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            async () =>
            {
                if (analyzeScan)
                {
                    await Resolve<DiskAnalysisViewModel>().ScanCommand.ExecuteAsync(null);

                    // Treemap yerleşimi ölçüm geçtikten sonra kuruluyor; çizilmesini bekliyoruz.
                    await Task.Delay(600);
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                }

                if (diskScan)
                {
                    await Resolve<DiskHealthViewModel>().LoadCommand.ExecuteAsync(null);

                    await Task.Delay(250);
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                }

                if (programScan)
                {
                    await Resolve<ProgramsViewModel>().LoadCommand.ExecuteAsync(null);

                    // Boyut ölçümü arkada sürüyor; görüntüde dolu satırlar görünsün.
                    await Task.Delay(3000);
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                }

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
