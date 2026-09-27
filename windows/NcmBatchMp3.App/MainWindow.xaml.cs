using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NcmBatchMp3.Core;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;

namespace NcmBatchMp3.App;

public partial class MainWindow : Window
{
    private readonly NcmConverter _converter = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(7) };
    private CancellationTokenSource? _conversionCancellation;
    private string? _ffmpegPath;
    private bool _isConverting;
    private bool _updateCheckRunning;
    private bool _isClosed;
    private int _pendingImports;
    private int _importGeneration;
    private CancellationTokenSource _importCancellation = new();
    private int _finished;
    private int _failed;
    private int _queued;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
        UpdateThemeIcon(ThemeManager.IsDarkMode);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NCM-Batch-MP3", CurrentVersion));
    }

    public BatchObservableCollection<QueueItem> QueueItems { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyWindowTheme(ThemeManager.IsDarkMode);
    }

    private static string CurrentVersion
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version is null ? "2.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var musicDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrWhiteSpace(musicDirectory))
        {
            musicDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        OutputDirectoryTextBox.Text = Path.Combine(musicDirectory, "NCM 转换输出");

        _ffmpegPath = FindFfmpeg();
        FfmpegStatusText.Text = _ffmpegPath is null ? "缺失" : "可用";
        AddLog("原生转换核心已就绪");
        UpdateInterface();

        await Task.Delay(1200);
        if (_isClosed) return;
        await CheckForUpdatesAsync(false);
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 NCM 文件",
            Filter = "网易云音乐 NCM (*.ncm)|*.ncm",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            await AddPathsAsync(dialog.FileNames);
        }
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择包含 NCM 文件的文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            await AddPathsAsync([dialog.SelectedPath]);
        }
    }

    private async Task AddPathsAsync(IEnumerable<string> paths)
    {
        if (_isConverting || _isClosed) return;
        var recursive = RecursiveCheckBox.IsChecked == true;
        var generation = _importGeneration;
        var cancellationToken = _importCancellation.Token;
        var inputs = paths.ToArray();
        _pendingImports++;
        UpdateInterface();
        OverallStatusText.Text = "正在读取文件…";
        try
        {
            var files = await Task.Run(() => CollectNcmFiles(inputs, recursive, cancellationToken), cancellationToken);
            if (generation != _importGeneration || _isClosed) return;
            var existing = new HashSet<string>(QueueItems.Select(item => item.FilePath), StringComparer.OrdinalIgnoreCase);
            var additions = files.Where(existing.Add).Select(path => new QueueItem(path)).ToArray();
            QueueItems.AddRange(additions);
            _queued += additions.Length;
            if (additions.Length > 0)
            {
                AddLog($"已添加 {additions.Length} 个 NCM 文件");
            }
            OverallStatusText.Text = additions.Length > 0 ? $"已添加 {additions.Length} 个文件" : "没有发现新的 NCM 文件";
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!_isClosed) AddLog($"读取失败 · {ShortError(error.Message)}");
        }
        finally
        {
            if (generation == _importGeneration && !_isClosed)
            {
                _pendingImports--;
                UpdateInterface();
            }
        }
    }

    private static IReadOnlyList<string> CollectNcmFiles(IEnumerable<string> paths, bool recursive, CancellationToken cancellationToken)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(candidate) && Path.GetExtension(candidate).Equals(".ncm", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(Path.GetFullPath(candidate));
                    continue;
                }

                if (!Directory.Exists(candidate))
                {
                    continue;
                }

                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = recursive,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
                    MatchCasing = MatchCasing.CaseInsensitive
                };
                foreach (var file in Directory.EnumerateFiles(candidate, "*.ncm", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results.Add(Path.GetFullPath(file));
                }
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or ArgumentException)
            {
                // Keep readable files from the same drop even if one folder is protected.
            }
        }

        return results.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_isConverting || QueueList.SelectedItem is not QueueItem selected)
        {
            return;
        }
        QueueItems.Remove(selected);
        RecountStats();
        UpdateInterface();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_isConverting)
        {
            return;
        }
        QueueItems.Clear();
        _importGeneration++;
        _importCancellation.Cancel();
        _importCancellation.Dispose();
        _importCancellation = new CancellationTokenSource();
        _pendingImports = 0;
        RecountStats();
        Logs.Clear();
        OverallProgressBar.Value = 0;
        OverallStatusText.Text = "就绪";
        UpdateInterface();
    }

    private void QueueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateInterface();
    }

    private void Tutorial_Click(object sender, RoutedEventArgs e)
    {
        new TutorialWindow { Owner = this }.ShowDialog();
    }

    private void ChooseOutput_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择输出目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = OutputDirectoryTextBox.Text
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            OutputDirectoryTextBox.Text = dialog.SelectedPath;
        }
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        var directory = OutputDirectoryTextBox.Text.Trim();
        if (directory.Length == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "无法打开输出目录", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_isConverting || QueueItems.Count == 0 || _pendingImports > 0)
        {
            return;
        }

        var outputDirectory = OutputDirectoryTextBox.Text.Trim();
        if (outputDirectory.Length == 0)
        {
            MessageBox.Show(this, "请选择输出目录。", "NCM 批量转 MP3", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "无法使用输出目录", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var batch = QueueItems.ToArray();
        foreach (var item in batch)
        {
            item.Status = QueueStatus.Queued;
            item.Detail = "等待";
            item.OutputPath = string.Empty;
            item.Progress = 0;
        }

        _conversionCancellation = new CancellationTokenSource();
        var cancellation = _conversionCancellation;
        _finished = 0;
        _failed = 0;
        _queued = batch.Length;
        SetConverting(true);
        AddLog($"开始转换 {QueueItems.Count} 个文件");
        var processed = 0;
        var failed = 0;
        var cancelled = false;

        var options = new ConversionOptions(
            outputDirectory,
            PreferMp3Radio.IsChecked == true ? OutputMode.PreferMp3 : OutputMode.Original,
            RenameCheckBox.IsChecked == true,
            OverwriteCheckBox.IsChecked == true);

        try
        {
            foreach (var item in batch)
            {
                if (cancellation.IsCancellationRequested)
                {
                    cancelled = true;
                    item.Status = QueueStatus.Cancelled;
                    item.Detail = "已取消";
                    _queued--;
                    continue;
                }

                item.Status = QueueStatus.Running;
                item.Detail = "解密中";
                QueueList.ScrollIntoView(item);
                UpdateStats();

                var itemIndex = processed;
                var progress = new Progress<ConversionProgress>(value =>
                {
                    if (_isClosed || cancellation.IsCancellationRequested || item.Status != QueueStatus.Running) return;
                    item.Progress = value.Fraction;
                    item.Detail = value.Phase switch { "transcode" => "转码中", "metadata" => "写入封面", _ => "解密中" };
                    OverallProgressBar.Value = (itemIndex + value.Fraction) / batch.Length;
                    OverallStatusText.Text = $"{Path.GetFileName(item.FilePath)} · {Math.Round(value.Fraction * 100)}%";
                });

                try
                {
                    var result = await Task.Run(() => _converter.ConvertAsync(
                        item.FilePath,
                        options,
                        _ffmpegPath,
                        cancellation.Token,
                        progress));
                    item.Status = QueueStatus.Finished;
                    item.Detail = result.Message;
                    item.OutputPath = result.OutputPath;
                    item.Progress = 1;
                    _finished++;
                    AddLog($"完成 · {Path.GetFileName(result.OutputPath)}");
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    item.Status = QueueStatus.Cancelled;
                    item.Detail = "已取消";
                }
                catch (Exception error)
                {
                    failed++;
                    _failed++;
                    item.Status = QueueStatus.Failed;
                    item.Detail = "失败";
                    AddLog($"失败 · {Path.GetFileName(item.FilePath)} · {ShortError(error.Message)}");
                }

                processed++;
                _queued--;
                OverallProgressBar.Value = processed / (double)batch.Length;
                UpdateStats();
            }

            OverallStatusText.Text = cancelled
                ? "转换已取消"
                : failed == 0
                    ? $"全部完成 · {processed} 个文件"
                    : $"转换完成 · {failed} 个失败";
            AddLog(cancelled ? "转换已取消" : "批量转换结束");
        }
        finally
        {
            if (!_isClosed) SetConverting(false);
            _conversionCancellation = null;
            cancellation.Dispose();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _conversionCancellation?.Cancel();
        CancelButton.IsEnabled = false;
        OverallStatusText.Text = "正在取消…";
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (_isConverting || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }
        await AddPathsAsync(paths);
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(true);
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.ToggleManualMode();
    }

    private void ThemeManager_ThemeChanged(bool darkMode)
    {
        UpdateThemeIcon(darkMode);
        if (new System.Windows.Interop.WindowInteropHelper(this).Handle != IntPtr.Zero)
        {
            ApplyWindowTheme(darkMode);
        }
    }

    private void ApplyWindowTheme(bool darkMode)
    {
        Background = MicaBackdrop.TryApply(this, darkMode)
            ? System.Windows.Media.Brushes.Transparent
            : new System.Windows.Media.SolidColorBrush(ThemeManager.FallbackWindowColor(darkMode));
    }

    private void UpdateThemeIcon(bool darkMode)
    {
        ThemeButton.Content = darkMode ? "\uE708" : "\uE706";
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateCheckRunning || _isClosed)
        {
            return;
        }
        _updateCheckRunning = true;

        try
        {
            using var response = await _httpClient.GetAsync(UpdateRules.LatestReleaseApi);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            var tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            var releaseUrl = root.TryGetProperty("html_url", out var url) ? url.GetString() : null;
            var officialUri = UpdateRules.OfficialReleaseUri(tagName, releaseUrl);
            if (_isClosed) return;
            if (officialUri is null)
            {
                throw new InvalidDataException("更新地址不可信");
            }

            if (UpdateRules.IsVersionNewer(tagName, CurrentVersion))
            {
                var answer = MessageBox.Show(
                    this,
                    $"NCM 批量转 MP3 {tagName} 已发布。\n\n前往 GitHub Release 下载最新版？",
                    "发现新版本",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (answer == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(officialUri.AbsoluteUri) { UseShellExecute = true });
                }
            }
            else if (manual)
            {
                MessageBox.Show(this, "当前已是最新版本。", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch when (!manual)
        {
            // Startup checks stay silent when the machine is offline.
        }
        catch
        {
            if (!_isClosed) MessageBox.Show(this, "暂时无法检查更新，请稍后再试。", "检查更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    private void EasterHeart_Click(object sender, RoutedEventArgs e)
    {
        EasterMessageText.Text = EasterEgg.Message(DateTime.Now);
        EasterOverlay.Visibility = Visibility.Visible;
    }

    private void EasterClose_Click(object sender, RoutedEventArgs e)
    {
        EasterOverlay.Visibility = Visibility.Collapsed;
    }

    private void EasterOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        EasterOverlay.Visibility = Visibility.Collapsed;
    }

    private void EasterCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void SetConverting(bool converting)
    {
        _isConverting = converting;
        AddFilesButton.IsEnabled = !converting;
        AddFolderButton.IsEnabled = !converting;
        ClearButton.IsEnabled = !converting && QueueItems.Count > 0;
        RemoveButton.IsEnabled = !converting && QueueList.SelectedItem is not null;
        StartButton.IsEnabled = !converting && QueueItems.Count > 0;
        CancelButton.IsEnabled = converting;
        OutputDirectoryTextBox.IsEnabled = !converting;
        PreferMp3Radio.IsEnabled = !converting;
        OriginalRadio.IsEnabled = !converting;
        RenameCheckBox.IsEnabled = !converting;
        OverwriteCheckBox.IsEnabled = !converting;
        RecursiveCheckBox.IsEnabled = !converting;
        UpdateInterface();
    }

    private void UpdateInterface()
    {
        EmptyState.Visibility = QueueItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueSummaryText.Text = $"{QueueItems.Count} 个文件";
        StartButton.IsEnabled = !_isConverting && _pendingImports == 0 && QueueItems.Count > 0;
        ClearButton.IsEnabled = !_isConverting && (QueueItems.Count > 0 || _pendingImports > 0);
        RemoveButton.IsEnabled = !_isConverting && QueueList.SelectedItem is not null;
        UpdateStats();
    }

    private void UpdateStats()
    {
        QueuedCountText.Text = _queued.ToString();
        FinishedCountText.Text = _finished.ToString();
        FailedCountText.Text = _failed.ToString();
    }

    private void RecountStats()
    {
        _queued = QueueItems.Count(item => item.Status is QueueStatus.Queued or QueueStatus.Running);
        _finished = QueueItems.Count(item => item.Status == QueueStatus.Finished);
        _failed = QueueItems.Count(item => item.Status == QueueStatus.Failed);
    }

    private void AddLog(string message)
    {
        if (_isClosed) return;
        Logs.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        while (Logs.Count > 200)
        {
            Logs.RemoveAt(0);
        }
        if (Logs.Count > 0)
        {
            LogList.ScrollIntoView(Logs[^1]);
        }
    }

    private static string ShortError(string message)
    {
        var line = message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "未知错误";
        return line.Length <= 120 ? line : line[..120] + "…";
    }

    private static string? FindFfmpeg()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _importCancellation.Cancel();
        _importCancellation.Dispose();
        ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;
        _conversionCancellation?.Cancel();
        _httpClient.Dispose();
        base.OnClosed(e);
    }
}
