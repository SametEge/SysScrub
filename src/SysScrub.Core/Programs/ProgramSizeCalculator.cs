using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SysScrub.Core.Programs;

/// <summary>Bir programın ölçülen boyutu.</summary>
public readonly record struct ProgramSize(string ProgramId, long Bytes);

/// <summary>
/// Kurulum klasörlerini tarayıp gerçek boyutu ölçer.
///
/// Kayıttaki <c>EstimatedSize</c> değerini programların çoğu hiç yazmıyor, yazanların
/// bir kısmı da kurulum anındaki değeri bırakıp güncellemiyor. Gerçek rakam ancak
/// klasör taranarak bulunur.
///
/// Ölçüm listeyi bekletmiyor: envanter hemen gösteriliyor, boyutlar arkada ölçülüp
/// satırlara düşüyor. Bulut yer tutucuları indirilmesin diye dosyanın gerçek
/// disk kaplaması değil, mantıksal boyutu okunuyor — indirme tetiklenmiyor.
/// </summary>
public sealed class ProgramSizeCalculator(ILogger<ProgramSizeCalculator>? logger = null)
{
    /// <summary>Aynı anda taranacak klasör sayısı. Fazlası diski kilitliyor, faydası yok.</summary>
    private const int MaxParallelism = 4;

    private readonly ILogger _logger = logger ?? NullLogger<ProgramSizeCalculator>.Instance;

    /// <summary>
    /// Verilen programların boyutunu ölçer ve her sonuç hazır olduğunda bildirir.
    /// </summary>
    public async Task MeasureAsync(
        IReadOnlyList<InstalledProgram> programs,
        IProgress<ProgramSize> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(programs);
        ArgumentNullException.ThrowIfNull(progress);

        InstalledProgram[] measurable = programs
            .Where(p => p.HasScannableLocation)
            // Büyükten küçüğe değil, listedeki sırayla: kullanıcı ekranda gördüğü
            // satırların dolmasını bekliyor.
            .ToArray();

        if (measurable.Length == 0)
        {
            return;
        }

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxParallelism,
            CancellationToken = cancellationToken
        };

        try
        {
            await Parallel.ForEachAsync(measurable, options, (program, token) =>
            {
                long bytes = Measure(program.InstallLocation!, token);

                if (bytes > 0)
                {
                    progress.Report(new ProgramSize(program.Id, bytes));
                }

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ölçüm iptal edilebilir bir kolaylık; yarıda kesilmesi hata değil.
        }
    }

    /// <summary>Tek bir klasörün toplam boyutu. Okunamayan alt ağaçlar sessizce atlanır.</summary>
    public long Measure(string directory, CancellationToken cancellationToken = default)
    {
        long total = 0;

        var pending = new Stack<string>();
        pending.Push(directory);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string current = pending.Pop();

            try
            {
                var info = new DirectoryInfo(current);

                // Bağlantı noktasının içine girmek aynı ağacı iki kez saymak ya da
                // bambaşka bir yeri bu programa yazmak demek.
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                foreach (FileInfo file in info.EnumerateFiles())
                {
                    if (!file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        total += file.Length;
                    }
                }

                foreach (DirectoryInfo child in info.EnumerateDirectories())
                {
                    pending.Push(child.FullName);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
            {
                // Erişemediğimiz klasör ölçüme girmez; tarama düşmez.
            }
        }

        return total;
    }
}
