using System.Collections.ObjectModel;
using Qingtu.Models;
using Qingtu.Services;
#if WINDOWS
using WinRT.Interop;
#endif

namespace Qingtu;

public partial class MainPage : ContentPage
{
    private readonly List<string> _files = [];
    private readonly ObservableCollection<string> _fileNames = [];
    private bool _isCompressTab = true;
    private bool _busy;

    private static readonly FilePickerFileType ImageTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"]
    });

    public MainPage()
    {
        InitializeComponent();
        FileList.ItemsSource = _fileNames;
        UpdateTabUi();
        UpdateRunLabel();
    }

    private void OnQualityChanged(object? sender, ValueChangedEventArgs e) =>
        TxtQuality.Text = ((int)e.NewValue).ToString();

    private void OnTabCompress(object? sender, EventArgs e)
    {
        _isCompressTab = true;
        UpdateTabUi();
    }

    private void OnTabStitch(object? sender, EventArgs e)
    {
        _isCompressTab = false;
        UpdateTabUi();
    }

    private void UpdateTabUi()
    {
        PanelCompress.IsVisible = _isCompressTab;
        TxtStitchHint.IsVisible = !_isCompressTab;
        StyleTab(BtnTabCompress, _isCompressTab);
        StyleTab(BtnTabStitch, !_isCompressTab);
        UpdateRunLabel();
    }

    private static void StyleTab(Button btn, bool active)
    {
        btn.FontAttributes = active ? FontAttributes.Bold : FontAttributes.None;
        btn.TextColor = active ? Color.FromArgb("#0D9488") : Color.FromArgb("#64748B");
        btn.BackgroundColor = active ? Color.FromArgb("#ECFDF5") : Colors.White;
        btn.BorderColor = active ? Color.FromArgb("#0D9488") : Color.FromArgb("#CBD5E1");
        btn.BorderWidth = 1;
    }

    private void UpdateRunLabel() =>
        BtnRun.Text = _isCompressTab ? $"开始压缩 ({_files.Count})" : $"开始拼接 ({_files.Count})";

    private async void OnPickFiles(object? sender, EventArgs e)
    {
        var picks = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            PickerTitle = "选择图片",
            FileTypes = ImageTypes
        });
        if (picks is null) return;
        AddFiles(picks.Select(f => f.FullPath));
    }

    private async void OnPickOutDir(object? sender, EventArgs e)
    {
        var path = await PickFolderAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        TxtOutDir.Text = path;
        ChkCustomOut.IsChecked = true;
    }

    private static async Task<string?> PickFolderAsync()
    {
#if WINDOWS
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
        {
            var hwnd = WindowNative.GetWindowHandle(native);
            InitializeWithWindow.Initialize(picker, hwnd);
        }
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
#else
        return await Task.FromResult<string?>(null);
#endif
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (!ImagePaths.IsImageFile(p) || _files.Contains(p)) continue;
            _files.Add(p);
            _fileNames.Add(Path.GetFileName(p));
        }

        TxtDropHint.Text = _files.Count > 0 ? $"已选 {_files.Count} 个文件" : "点击下方选择图片";
        TxtDropHint.TextColor = _files.Count > 0 ? Color.FromArgb("#0D9488") : Color.FromArgb("#64748B");
        UpdateRunLabel();
    }

    private JobOptions BuildOptions()
    {
        _ = int.TryParse(TxtMaxSide.Text?.Trim(), out var maxSide);
        if (maxSide < 0) maxSide = 0;
        return new JobOptions
        {
            Quality = (int)SliderQuality.Value,
            MaxSide = maxSide,
            Format = GetFormat(),
            OutputDir = ChkCustomOut.IsChecked && !string.IsNullOrWhiteSpace(TxtOutDir.Text)
                ? TxtOutDir.Text.Trim()
                : null
        };
    }

    private OutputFormat GetFormat()
    {
        if (FmtJpeg.IsChecked) return OutputFormat.Jpeg;
        if (FmtPng.IsChecked) return OutputFormat.Png;
        if (FmtWebp.IsChecked) return OutputFormat.WebP;
        return OutputFormat.KeepOriginal;
    }

    private async void OnRun(object? sender, EventArgs e)
    {
        if (_busy) return;
        if (_files.Count == 0)
        {
            TxtLog.Text = "请先添加图片";
            return;
        }

        _busy = true;
        BtnRun.IsEnabled = false;
        try
        {
            if (_isCompressTab)
            {
                var opts = BuildOptions();
                var results = await Task.Run(() => ImageCompressService.RunJobs(_files, opts));
                TxtLog.Text = FormatResults.FormatLog(results);
            }
            else
            {
                if (_files.Count < 2)
                {
                    TxtLog.Text = "拼接需要至少 2 张图";
                    return;
                }

                var outPath = await Task.Run(() =>
                {
                    var opts = BuildOptions();
                    var path = !string.IsNullOrWhiteSpace(opts.OutputDir)
                        ? Path.Combine(opts.OutputDir!, "stitched.png")
                        : Path.Combine(ImagePaths.DefaultOutputDir(_files[0]), "stitched.png");
                    ImageStitchService.StitchHorizontal(_files, path);
                    return path;
                });
                TxtLog.Text = $"拼接完成 → {outPath}";
            }
        }
        catch (Exception ex)
        {
            TxtLog.Text = ex.Message;
        }
        finally
        {
            _busy = false;
            BtnRun.IsEnabled = true;
        }
    }

    private void OnClear(object? sender, EventArgs e)
    {
        _files.Clear();
        _fileNames.Clear();
        TxtLog.Text = "";
        TxtDropHint.Text = "点击下方选择图片";
        TxtDropHint.TextColor = Color.FromArgb("#64748B");
        UpdateRunLabel();
    }
}
