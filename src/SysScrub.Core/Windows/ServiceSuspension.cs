using System.ServiceProcess;
using Microsoft.Extensions.Logging;

namespace SysScrub.Core.Windows;

/// <summary>
/// Bir işlem boyunca servisleri geçici olarak durdurur ve sonunda eski hâline döndürür.
///
/// Windows Update önbelleği servis çalışırken kilitli olduğu için temizlenemiyor.
/// Servisi durdurup temizleyip geri başlatmak, kullanıcıdan bir şey istemeden çalışan tek yol.
/// Zaten çalışmıyorsa dokunulmaz — kapalı bir servisi biz başlatmış olmayız.
/// </summary>
public sealed class ServiceSuspension : IAsyncDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private readonly List<string> _stopped = [];
    private readonly ILogger? _logger;

    private ServiceSuspension(ILogger? logger) => _logger = logger;

    /// <summary>Hiçbir şey yapmayan örnek; işleyicisi olmayan kurallar bunu kullanır.</summary>
    public static ServiceSuspension None { get; } = new(null);

    public static async Task<ServiceSuspension> SuspendAsync(
        IReadOnlyList<string> serviceNames,
        ILogger? logger,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var suspension = new ServiceSuspension(logger);

        if (dryRun)
        {
            return suspension;
        }

        foreach (string name in serviceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var controller = new ServiceController(name);

                if (controller.Status != ServiceControllerStatus.Running)
                {
                    continue;
                }

                if (!controller.CanStop)
                {
                    logger?.LogWarning("{Service} servisi durdurulamıyor", name);
                    continue;
                }

                controller.Stop();
                await Task.Run(
                    () => controller.WaitForStatus(ServiceControllerStatus.Stopped, WaitTimeout),
                    cancellationToken).ConfigureAwait(false);

                suspension._stopped.Add(name);
                logger?.LogInformation("{Service} servisi geçici olarak durduruldu", name);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.ServiceProcess.TimeoutException)
            {
                // Servis yoksa, yetki yetmiyorsa veya zamanında durmadıysa temizlik yine denenir;
                // kilitli dosyalar yeniden başlatmaya işaretlenerek yakalanır.
                logger?.LogWarning(ex, "{Service} servisi durdurulamadı", name);
            }
        }

        return suspension;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (string name in _stopped)
        {
            try
            {
                using var controller = new ServiceController(name);

                if (controller.Status == ServiceControllerStatus.Stopped)
                {
                    controller.Start();
                    await Task.Run(() => controller.WaitForStatus(ServiceControllerStatus.Running, WaitTimeout))
                        .ConfigureAwait(false);
                }

                _logger?.LogInformation("{Service} servisi yeniden başlatıldı", name);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.ServiceProcess.TimeoutException)
            {
                // Servis geri başlatılamadıysa kullanıcıya söylemek gerek: Windows Update çalışmaz.
                _logger?.LogError(ex, "{Service} servisi yeniden başlatılamadı — elle başlatılması gerekebilir", name);
            }
        }

        _stopped.Clear();
    }
}
