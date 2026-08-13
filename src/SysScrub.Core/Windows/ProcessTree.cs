using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SysScrub.Core.Windows;

/// <summary>Süreç ağacının nasıl sonlandığı.</summary>
public enum ProcessTreeOutcome
{
    /// <summary>Ağaçtaki tüm süreçler bitti.</summary>
    Completed,

    /// <summary>Süreç başlatılamadı.</summary>
    NotStarted,

    /// <summary>Zaman aşımına uğradı; süreç hâlâ çalışıyor olabilir.</summary>
    TimedOut,

    /// <summary>Kullanıcı beklemeyi iptal etti; süreç çalışmaya devam ediyor.</summary>
    Cancelled
}

public sealed record ProcessTreeResult(ProcessTreeOutcome Outcome, int ExitCode, string? Message);

/// <summary>
/// Bir süreci ve türettiği tüm alt süreçleri bekler.
///
/// Neden gerekli: kaldırıcıların çoğu doğrudan iş yapmıyor. Inno Setup'ın
/// <c>unins000.exe</c> dosyası kendini geçici klasöre kopyalayıp oradan çalıştırıyor
/// ve hemen çıkıyor. Sadece başlattığımız süreci beklersek kaldırma daha başlamadan
/// "bitti" deriz.
///
/// Çözüm Windows'un iş nesnesi (job object): başlatılan süreç bir işe atanıyor,
/// alt süreçler işi devralıyor ve işteki etkin süreç sayısı sıfırlanana kadar
/// bekleniyor.
/// </summary>
public static class ProcessTree
{
    /// <summary>JobObjectBasicAccountingInformation bilgi sınıfı.</summary>
    private const uint BasicAccountingInformationClass = 1;

    /// <summary>Etkin süreç sayısı bu aralıklarla sorgulanıyor.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Süreci başlatır ve ağacın tamamının bitmesini bekler.
    /// Kaldırıcı penceresi kullanıcıya görünür: sessiz mod istenmişse komutun
    /// kendisi sessizdir, biz pencereyi gizlemeyiz.
    /// </summary>
    public static async Task<ProcessTreeResult> RunAndWaitAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = false
                }
            };

            try
            {
                if (!process.Start())
                {
                    return new ProcessTreeResult(ProcessTreeOutcome.NotStarted, -1, "Süreç başlatılamadı.");
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return new ProcessTreeResult(ProcessTreeOutcome.NotStarted, -1, ex.Message);
            }

            // Atama başlatmadan sonra yapılıyor: süreci askıya alarak başlatmak
            // CreateProcess'i doğrudan çağırmayı gerektirirdi. Kaldırıcıların
            // kendini kopyalaması milisaniyeler değil saniyeler sürüyor, bu yüzden
            // pratikte alt süreçler işe yetişiyor.
            if (job != IntPtr.Zero)
            {
                AssignProcessToJobObject(job, process.Handle);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            int exitCode = process.ExitCode;

            // İş nesnesi oluşturulamadıysa elimizdeki tek bilgi ilk sürecin çıkışı.
            if (job == IntPtr.Zero)
            {
                return new ProcessTreeResult(ProcessTreeOutcome.Completed, exitCode, null);
            }

            ProcessTreeOutcome outcome = await WaitForJobAsync(job, timeout, cancellationToken)
                .ConfigureAwait(false);

            return new ProcessTreeResult(outcome, exitCode, null);
        }
        finally
        {
            if (job != IntPtr.Zero)
            {
                CloseHandle(job);
            }
        }
    }

    private static async Task<ProcessTreeOutcome> WaitForJobAsync(
        IntPtr job,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ProcessTreeOutcome.Cancelled;
            }

            if (ActiveProcessCount(job) == 0)
            {
                return ProcessTreeOutcome.Completed;
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ProcessTreeOutcome.Cancelled;
            }
        }

        return ProcessTreeOutcome.TimedOut;
    }

    /// <summary>Sorgu başarısız olursa 0 dönüyoruz: sonsuza kadar beklemekten iyidir.</summary>
    private static int ActiveProcessCount(IntPtr job)
    {
        var info = new JobObjectBasicAccountingInformation();
        int size = Marshal.SizeOf(info);
        IntPtr buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(info, buffer, fDeleteOld: false);

            if (!QueryInformationJobObject(job, BasicAccountingInformationClass, buffer, size, IntPtr.Zero))
            {
                return 0;
            }

            return Marshal.PtrToStructure<JobObjectBasicAccountingInformation>(buffer).ActiveProcesses;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicAccountingInformation
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public int TotalPageFaultCount;
        public int TotalProcesses;
        public int ActiveProcesses;
        public int TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        IntPtr job,
        uint informationClass,
        IntPtr information,
        int informationLength,
        IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
