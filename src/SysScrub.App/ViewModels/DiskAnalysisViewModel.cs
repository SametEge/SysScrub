using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SysScrub.App.Localization;
using static SysScrub.App.Localization.L;
using SysScrub.Core.Analysis;
using SysScrub.Core.Formatting;
using SysScrub.Core.Windows;

namespace SysScrub.App.ViewModels;

/// <summary>Üstteki sürücü seçici düğmesi.</summary>
public sealed partial class DriveChoiceViewModel(string root, string label, long freeBytes, long totalBytes)
    : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string Root { get; } = root;

    public string Label { get; } = label;

    public string Detail { get; } = totalBytes > 0
        ? T("Da_DriveDetail", ByteSize.Format(freeBytes), ByteSize.Format(totalBytes))
        : string.Empty;
}

/// <summary>Ekmek kırıntısı çubuğundaki tek bir adım.</summary>
public sealed record BreadcrumbStep(FolderNode Node, string Label, bool IsLast);

/// <summary>
/// Disk analizi ekranı.
///
/// Salt-okunur: hiçbir dosya silinmiyor, taşınmıyor, açılmıyor. Bulut yer
/// tutucuları indirilmiyor — indirilseydi "alanı ne yiyor" sorusunu cevaplamak
/// için gigabaytlarca veri çekmiş olurduk.
///
/// Yinelenen dosya bulucu da yalnızca rapor üretiyor; silme kararı kullanıcının
/// ve her gruptan en az bir kopya her zaman korunuyor.
/// </summary>
public sealed partial class DiskAnalysisViewModel : ObservableObject
{
    private readonly DiskScanner _scanner;
    private readonly DuplicateFinder _duplicates;
    private readonly ILogger<DiskAnalysisViewModel> _logger;

    private CancellationTokenSource? _cancellation;
    private DiskScanResult _result = DiskScanResult.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyTitle = string.Empty;

    [ObservableProperty]
    private string _busyDetail = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    private FolderNode? _current;

    [ObservableProperty]
    private bool _showDuplicates;

    [ObservableProperty]
    private DuplicateScanResult _duplicateResult = DuplicateScanResult.Empty;

    public DiskAnalysisViewModel(
        DiskScanner scanner,
        DuplicateFinder duplicates,
        ILogger<DiskAnalysisViewModel> logger)
    {
        _scanner = scanner;
        _duplicates = duplicates;
        _logger = logger;

        // Dil değişince tüm metinler yeniden okunmalı; boş ad her bağlamayı tazeliyor.
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);

        Drives = [];
        Breadcrumb = [];
        LargestFiles = [];
        Types = [];
        DuplicateGroups = [];

        LoadDrives();
    }

    public ObservableCollection<DriveChoiceViewModel> Drives { get; }

    public ObservableCollection<BreadcrumbStep> Breadcrumb { get; }

    public ObservableCollection<FolderNode> LargestFiles { get; }

    public ObservableCollection<FileTypeSummary> Types { get; }

    public ObservableCollection<DuplicateGroup> DuplicateGroups { get; }

    public bool HasResult => HasScanned && Current is not null;

    public bool CanGoUp => Current?.Parent is not null;

    public string TotalLabel => ByteSize.Format(_result.TotalBytes);

    public string CurrentLabel => Current is null ? string.Empty : ByteSize.Format(Current.SizeBytes);

    public string HeadlineText
    {
        get
        {
            if (!HasScanned)
            {
                return T("Da_NotScanned");
            }

            return Current is { } node && node.Parent is not null
                ? $"{node.Name} — {ByteSize.Format(node.SizeBytes)}"
                : T("Da_InUse", TotalLabel);
        }
    }

    public string HeadlineDetail
    {
        get
        {
            if (!HasScanned)
            {
                return T("Da_Intro");
            }

            var parts = new List<string>
            {
                T("Da_Files", $"{_result.FileCount:N0}"),
                T("Da_Folders", $"{_result.DirectoryCount:N0}"),
                DurationText.FromMilliseconds((int)_result.Duration.TotalMilliseconds)
            };

            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Atlananlar sessizce yutulmuyor; kullanıcı neyin sayılmadığını bilmeli.</summary>
    public string SkippedMessage
    {
        get
        {
            if (!HasScanned)
            {
                return string.Empty;
            }

            var parts = new List<string>();

            if (_result.SkippedDirectories > 0)
            {
                parts.Add(T("Da_SkippedDirs", $"{_result.SkippedDirectories:N0}"));
            }

            if (_result.CloudPlaceholders > 0)
            {
                parts.Add(T("Da_SkippedCloud", $"{_result.CloudPlaceholders:N0}"));
            }

            if (_result.SkippedLinks > 0)
            {
                parts.Add(T("Da_SkippedLinks", $"{_result.SkippedLinks:N0}"));
            }

            return parts.Count == 0 ? string.Empty : string.Join("  ·  ", parts);
        }
    }

    public bool HasSkipped => SkippedMessage.Length > 0;

    public bool HasDuplicateResult => DuplicateGroups.Count > 0;

    public string DuplicateSummary => DuplicateResult.Groups.Count == 0
        ? string.Empty
        : T("Da_DupSummary",
            $"{DuplicateResult.Groups.Count:N0}",
            $"{DuplicateResult.DuplicateCount:N0}",
            ByteSize.Format(DuplicateResult.RecoverableBytes));

    // ------------------------------------------------------------------ sürücüler

    private void LoadDrives()
    {
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }

                Drives.Add(new DriveChoiceViewModel(
                    drive.RootDirectory.FullName,
                    drive.Name.TrimEnd('\\'),
                    drive.AvailableFreeSpace,
                    drive.TotalSize));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Okunamayan sürücü listede yer almaz; tarama zaten yapılamazdı.
            }
        }

        if (Drives.Count > 0)
        {
            Drives[0].IsSelected = true;
        }
    }

    private string? SelectedRoot => Drives.FirstOrDefault(d => d.IsSelected)?.Root;

    [RelayCommand]
    private void SelectDrive(DriveChoiceViewModel drive)
    {
        foreach (DriveChoiceViewModel item in Drives)
        {
            item.IsSelected = ReferenceEquals(item, drive);
        }
    }

    // ------------------------------------------------------------------ tarama

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ScanAsync()
    {
        if (SelectedRoot is not { Length: > 0 } root)
        {
            return;
        }

        IsBusy = true;
        BusyTitle = T("Da_Busy_Scan");
        BusyDetail = root;
        StatusMessage = string.Empty;
        ShowDuplicates = false;

        DuplicateGroups.Clear();
        DuplicateResult = DuplicateScanResult.Empty;

        _cancellation = new CancellationTokenSource();

        var progress = new Progress<DiskScanProgress>(p =>
            BusyDetail = T("Da_ScanProgress", p.Files.ToString("N0"), ByteSize.Format(p.Bytes)) +
                         "\n" + p.CurrentPath);

        try
        {
            _result = await _scanner.ScanAsync(root, progress, _cancellation.Token);

            HasScanned = true;
            Navigate(_result.Root);

            LargestFiles.Clear();

            foreach (FolderNode file in _result.LargestFiles.Take(50))
            {
                LargestFiles.Add(file);
            }

            Types.Clear();

            foreach (FileTypeSummary type in _result.TypeBreakdown.Take(15))
            {
                Types.Add(type);
            }

            StatusMessage = T(
                "Da_ScanResult",
                ByteSize.Format(_result.TotalBytes),
                $"{_result.FileCount:N0}",
                $"{_result.DirectoryCount:N0}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = T("Msg_Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disk analizi başarısız");
            StatusMessage = T("Err_ScanFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            Finish();
        }
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand]
    private void Cancel() => _cancellation?.Cancel();

    // ------------------------------------------------------------------ gezinme

    [RelayCommand]
    private void Open(FolderNode? node)
    {
        if (node is { IsFile: false, HasChildren: true })
        {
            Navigate(node);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void GoUp()
    {
        if (Current?.Parent is { } parent)
        {
            Navigate(parent);
        }
    }

    private void Navigate(FolderNode node)
    {
        Current = node;

        Breadcrumb.Clear();

        IReadOnlyList<FolderNode> path = node.PathFromRoot();

        for (int i = 0; i < path.Count; i++)
        {
            Breadcrumb.Add(new BreadcrumbStep(path[i], path[i].Name, i == path.Count - 1));
        }

        GoUpCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(CurrentLabel));
        OnPropertyChanged(nameof(HeadlineText));
    }

    // ------------------------------------------------------------------ yinelenenler

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task FindDuplicatesAsync()
    {
        if (!HasScanned)
        {
            return;
        }

        IsBusy = true;
        BusyTitle = T("Da_Busy_Dup");
        BusyDetail = T("Da_Busy_DupDetail");

        _cancellation = new CancellationTokenSource();

        var progress = new Progress<DuplicateScanProgress>(p =>
            BusyDetail = p.Total > 0 ? $"{p.Stage}\n{p.Processed:N0} / {p.Total:N0}" : p.Stage);

        try
        {
            // Tarama ağacı üzerinden çalışıyor: diski ikinci kez gezmeye gerek yok.
            DuplicateResult = await _duplicates.FindAsync(_result.Root, progress, _cancellation.Token);

            DuplicateGroups.Clear();

            foreach (DuplicateGroup group in DuplicateResult.Groups.Take(200))
            {
                DuplicateGroups.Add(group);
            }

            ShowDuplicates = true;

            StatusMessage = DuplicateResult.Groups.Count == 0
                ? T("Da_NoDuplicates")
                : T("Da_DupResult",
                    $"{DuplicateResult.Groups.Count:N0}",
                    $"{DuplicateResult.DuplicateCount:N0}",
                    ByteSize.Format(DuplicateResult.RecoverableBytes));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = T("Msg_Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yinelenen arama başarısız");
            StatusMessage = T("Err_SearchFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            Finish();
        }
    }

    [RelayCommand]
    private void ToggleDuplicates() => ShowDuplicates = !ShowDuplicates;

    /// <summary>
    /// Seçili yinelenen kopyaları Geri Dönüşüm Kutusu'na taşır.
    ///
    /// Her gruptan ilk kopya her zaman korunuyor — bu kilit isteğe bağlı değil.
    /// Kalıcı silme de yapılmıyor: yanlış dosya gittiyse kullanıcı geri alabilsin.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private void RemoveDuplicates(DuplicateGroup group)
    {
        if (group is null || group.Paths.Count < 2)
        {
            return;
        }

        string[] extras = group.Paths.Skip(1).Where(File.Exists).ToArray();

        if (extras.Length == 0)
        {
            return;
        }

        bool removed = ShellFileOperations.DeleteToRecycleBin(extras);

        if (removed)
        {
            DuplicateGroups.Remove(group);

            StatusMessage = T(
                "Da_DupRemoved",
                extras.Length,
                ByteSize.Format(group.RecoverableBytes),
                group.Paths[0]);
        }
        else
        {
            StatusMessage = T("Da_DupFailed");
        }

        OnPropertyChanged(nameof(HasDuplicateResult));
    }

    private void Finish()
    {
        _cancellation?.Dispose();
        _cancellation = null;

        ScanCommand.NotifyCanExecuteChanged();
        FindDuplicatesCommand.NotifyCanExecuteChanged();
        RemoveDuplicatesCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(TotalLabel));
        OnPropertyChanged(nameof(HeadlineText));
        OnPropertyChanged(nameof(HeadlineDetail));
        OnPropertyChanged(nameof(SkippedMessage));
        OnPropertyChanged(nameof(HasSkipped));
        OnPropertyChanged(nameof(HasDuplicateResult));
        OnPropertyChanged(nameof(DuplicateSummary));
    }

    partial void OnIsBusyChanged(bool value) => Finish();

    partial void OnDuplicateResultChanged(DuplicateScanResult value) =>
        OnPropertyChanged(nameof(DuplicateSummary));
}
