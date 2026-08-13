using SysScrub.Core.Formatting;

namespace SysScrub.Core.Analysis;

/// <summary>Taranan ağaçtaki tek bir klasör ya da dosya.</summary>
public sealed class FolderNode
{
    private readonly List<FolderNode> _children = [];

    public required string Name { get; init; }

    public required string FullPath { get; init; }

    /// <summary>Dosyaysa kendi boyutu, klasörse altındaki her şeyin toplamı.</summary>
    public long SizeBytes { get; set; }

    public int FileCount { get; set; }

    public bool IsFile { get; init; }

    public FolderNode? Parent { get; set; }

    /// <summary>Alt öğeler, büyükten küçüğe sıralı.</summary>
    public IReadOnlyList<FolderNode> Children => _children;

    public bool HasChildren => _children.Count > 0;

    public string SizeLabel => ByteSize.Format(SizeBytes);

    /// <summary>Üst klasöre göre kapladığı oran; treemap ve yüzde etiketleri bunu kullanır.</summary>
    public double ShareOfParent => Parent is { SizeBytes: > 0 }
        ? (double)SizeBytes / Parent.SizeBytes
        : 0;

    public void Add(FolderNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    /// <summary>Alt öğeleri büyükten küçüğe sıralar; treemap yerleşimi bunu bekliyor.</summary>
    public void SortChildren()
    {
        _children.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));

        foreach (FolderNode child in _children)
        {
            child.SortChildren();
        }
    }

    /// <summary>Kökten bu düğüme kadarki yol; ekmek kırıntısı çubuğu için.</summary>
    public IReadOnlyList<FolderNode> PathFromRoot()
    {
        var path = new List<FolderNode>();

        for (FolderNode? node = this; node is not null; node = node.Parent)
        {
            path.Insert(0, node);
        }

        return path;
    }

    public override string ToString() => $"{Name} ({SizeLabel})";
}

/// <summary>Dosya türüne göre toplam.</summary>
public sealed record FileTypeSummary(string Extension, long SizeBytes, int Count)
{
    public string Label => Extension.Length == 0 ? CoreText.Get("Da_NoExtension", "uzantısız") : Extension;

    public string SizeLabel => ByteSize.Format(SizeBytes);
}

/// <summary>Tek bir taramanın sonucu.</summary>
public sealed record DiskScanResult
{
    public required FolderNode Root { get; init; }

    public required TimeSpan Duration { get; init; }

    public required int FileCount { get; init; }

    public required int DirectoryCount { get; init; }

    /// <summary>Erişilemediği için atlanan klasör sayısı; sessizce yutulmuyor.</summary>
    public int SkippedDirectories { get; init; }

    /// <summary>
    /// Bulut yer tutucusu sayısı. Bu dosyalar diskte yer kaplamıyor; boyuta
    /// katılsalardı "alanı ne yiyor" sorusunun cevabı yanlış çıkardı.
    /// </summary>
    public int CloudPlaceholders { get; init; }

    /// <summary>Takip edilmeyen bağlantı noktası sayısı (junction/symlink).</summary>
    public int SkippedLinks { get; init; }

    public IReadOnlyList<FolderNode> LargestFiles { get; init; } = [];

    public IReadOnlyList<FileTypeSummary> TypeBreakdown { get; init; } = [];

    public long TotalBytes => Root.SizeBytes;

    public static DiskScanResult Empty { get; } = new()
    {
        Root = new FolderNode { Name = string.Empty, FullPath = string.Empty },
        Duration = TimeSpan.Zero,
        FileCount = 0,
        DirectoryCount = 0
    };
}

/// <summary>Tarama ilerlemesi.</summary>
public sealed record DiskScanProgress(string CurrentPath, int Files, long Bytes);
