using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SysScrub.App.Services;
using SysScrub.App.ViewModels;
using SysScrub.App.Views;
using SysScrub.Core.System;

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

                services.AddSingleton<MainWindowViewModel>();
                services.AddTransient<DashboardViewModel>();

                services.AddSingleton<MainWindow>();
            })
            .Build();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        Resolve<ThemeService>().Initialize();

        Log.Information("SysScrub başlatıldı, sürüm {Version}", GetType().Assembly.GetName().Version);

        MainWindow window = Resolve<MainWindow>();
        window.Show();

        if (WindowCapture.PathFromArgs(e.Args) is { } screenshotPath)
        {
            CaptureAndExit(window, screenshotPath);
        }
    }

    /// <summary>Geliştirme anahtarı: pencere çizildikten sonra ekran görüntüsünü alıp çıkar.</summary>
    private void CaptureAndExit(Window window, string screenshotPath)
    {
        window.ContentRendered += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            () =>
            {
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
