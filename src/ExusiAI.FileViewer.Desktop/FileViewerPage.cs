using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using ExusiAI.FileViewer.Core;
using Microsoft.Win32;

namespace ExusiAI.FileViewer.Desktop;

internal sealed class FileViewerPage : UserControl, IDisposable
{
    private const int MaximumTextPreviewCharacters = 8 * 1024 * 1024;
    private const int MaximumPrintCharacters = 2 * 1024 * 1024;
    private const int SlideThumbnailBatchSize = 16;

    private FileViewerProviderRegistry? providers;

    private FileViewerProviderRegistry Providers => providers ??= new(new IFileViewerProvider[]
    {
        new TextFileViewerProvider(),
        new CsvFileViewerProvider(),
        new DocxFileViewerProvider(),
        new XlsxFileViewerProvider(),
        new PptxFileViewerProvider()
    });

    private readonly RecentFilesStore recentFilesStore = new();
    private readonly ViewerSettings settings;
    private readonly ObservableCollection<string> tableRows = [];
    private readonly ObservableCollection<SearchResultOption> searchResults = [];
    private readonly ObservableCollection<SlideThumbnailOption> slideThumbnails = [];
    private readonly ObservableCollection<MarkdownOutlineOption> markdownOutline = [];
    private readonly Dictionary<int, Block> markdownBlocksByLine = [];
    private IReadOnlyList<RecentFileEntry> recentEntries = [];

    private readonly TextBlock title = new()
    {
        FontSize = 19,
        FontWeight = FontWeights.SemiBold,
        Text = "尚未打开文件",
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private readonly TextBlock documentMeta = new()
    {
        FontSize = 11,
        Opacity = 0.7,
        Text = "安全只读查看器",
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private readonly TextBlock status = new()
    {
        FontSize = 11,
        Opacity = 0.72,
        Text = "TXT · Markdown · CSV · DOCX · XLSX · PPTX",
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly TextBlock textStatistics = new()
    {
        FontSize = 11,
        Opacity = 0.72,
        Margin = new Thickness(16, 0, 16, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed
    };

    private readonly TextBox textPreview = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        AcceptsTab = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = 15,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Padding = new Thickness(28, 24, 28, 32),
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Visibility = Visibility.Collapsed
    };

    private readonly FlowDocumentScrollViewer markdownPreview = new()
    {
        IsToolBarVisible = false,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Visibility = Visibility.Collapsed
    };

    private readonly TextBlock slideTitle = new()
    {
        FontSize = 30,
        FontWeight = FontWeights.SemiBold,
        LineHeight = 39,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 20),
        Visibility = Visibility.Collapsed
    };

    private readonly TextBlock slideBody = new()
    {
        FontSize = 20,
        LineHeight = 32,
        TextWrapping = TextWrapping.Wrap
    };

    private readonly TextBlock slideEmpty = new()
    {
        FontSize = 16,
        Opacity = 0.65,
        Text = "此页没有可提取的文本内容。",
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed
    };

    private readonly Canvas slideVisualCanvas = new()
    {
        Width = 960,
        Height = 540,
        ClipToBounds = true,
        Visibility = Visibility.Collapsed
    };

    private readonly ScrollViewer slideScroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Visibility = Visibility.Collapsed
    };

    private readonly ListBox tablePreview;
    private readonly ListBox searchResultList;
    private readonly ListBox slideThumbnailList;
    private readonly ListBox markdownOutlineList;

    private readonly Border searchPane = new()
    {
        Width = 310,
        Margin = new Thickness(0, 16, 16, 16),
        Padding = new Thickness(14),
        CornerRadius = new CornerRadius(10),
        Visibility = Visibility.Collapsed
    };
    private readonly StackPanel documentInfoContent = new();
    private readonly Border documentInfoPane = new()
    {
        Width = 330,
        Margin = new Thickness(0, 16, 16, 16),
        Padding = new Thickness(18),
        CornerRadius = new CornerRadius(10),
        Visibility = Visibility.Collapsed
    };
    private readonly StackPanel welcomeContent = new();
    private readonly Border welcomePanel;
    private readonly TextBlock modeChipText = new()
    {
        Text = "只读",
        FontSize = 11,
        FontWeight = FontWeights.SemiBold
    };

    private readonly Button saveButton = CreateSecondaryButton("保存");
    private readonly Button saveAsButton = CreateSecondaryButton("另存为");
    private readonly Button editButton = CreateSecondaryButton("启用编辑");
    private readonly Button undoButton = CreateSecondaryButton("撤销");
    private readonly Button redoButton = CreateSecondaryButton("重做");
    private readonly Button cutButton = CreateSecondaryButton("剪切");
    private readonly Button copyButton = CreateSecondaryButton("复制");
    private readonly Button pasteButton = CreateSecondaryButton("粘贴");
    private readonly Button selectAllButton = CreateSecondaryButton("全选");
    private readonly Button resetZoomButton = CreateSecondaryButton("重置缩放");
    private readonly Button loadMoreButton = CreateSecondaryButton("加载下一页");
    private readonly Button previousSlideButton = CreateSecondaryButton("上一页");
    private readonly Button nextSlideButton = CreateSecondaryButton("下一页");
    private readonly Button cancelButton = CreateSecondaryButton("取消");
    private readonly Button reloadButton = CreateSecondaryButton("重新加载");
    private readonly Button documentInfoButton = CreateSecondaryButton("文档信息");
    private readonly Button openFolderButton = CreateSecondaryButton("打开位置");
    private readonly Button copyPathButton = CreateSecondaryButton("复制路径");
    private readonly Button clearRecentButton = CreateSecondaryButton("清除最近记录");
    private readonly Button previousSearchButton = CreateSecondaryButton("上一项");
    private readonly Button nextSearchButton = CreateSecondaryButton("下一项");
    private readonly Button wrapTextButton = CreateSecondaryButton("自动换行");
    private readonly Button exportButton = CreateSecondaryButton("导出内容");
    private readonly Button printButton = CreateSecondaryButton("打印 / PDF");
    private readonly Button loadMoreSlidesButton = CreateSecondaryButton("更多幻灯片");
    private readonly Button goToLineButton = CreateSecondaryButton("转到行");
    private readonly Button markdownPreviewButton = CreateSecondaryButton("Markdown 预览");
    private readonly TextBox goToLineBox = new() { Width = 76, ToolTip = "输入文本行号并按 Enter" };
    private readonly Border slideNavigationPane = new()
    {
        Width = 210,
        Margin = new Thickness(16, 16, 0, 16),
        Padding = new Thickness(10),
        CornerRadius = new CornerRadius(10),
        Visibility = Visibility.Collapsed
    };
    private readonly TextBlock navigationPaneTitle = new()
    {
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(5, 3, 5, 10)
    };
    private readonly TextBox slideNumberBox = new() { Width = 56, ToolTip = "输入幻灯片页码并按 Enter" };
    private readonly TextBox searchBox = new() { Width = 230, ToolTip = "搜索当前文档全部可索引内容" };
    private readonly ComboBox recentFilesBox = new() { Width = 205, ToolTip = "最近打开" };
    private readonly ComboBox worksheetBox = new() { Width = 170, ToolTip = "切换工作表", Visibility = Visibility.Collapsed };
    private readonly Slider zoom = new()
    {
        Minimum = 11,
        Maximum = 28,
        Value = 15,
        Width = 112,
        TickFrequency = 1,
        IsSnapToTickEnabled = true
    };

    private CancellationTokenSource? loadCancellation;
    private CancellationTokenSource? operationCancellation;
    private ViewerDocument? document;
    private IAsyncEnumerator<TabularPage>? tablePages;
    private int currentSlideNumber;
    private bool disposed;
    private bool changingWorksheet;
    private bool loadingTextPreview;
    private bool textPreviewFullyLoaded;
    private bool isDirty;
    private bool externalChangePending;
    private bool changingSlideThumbnailSelection;
    private bool isMarkdownDocument;
    private bool markdownPreviewMode;
    private MarkdownParseResult? markdownParseResult;
    private FileSystemWatcher? fileWatcher;
    private DateTime lastKnownWriteTimeUtc;
    private readonly System.Windows.Threading.DispatcherTimer statisticsTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    private Action? showHomeCommands;
    private Border? topBar;
    private Border? bottomBar;
    private Border? documentCanvas;

    public event EventHandler<string>? DocumentOpened;
    public bool HasDocument => document is not null;
    public bool CanNavigateSlides => document is ISlidePreviewDocument;

    public FileViewerPage(ViewerSettings settings)
    {
        this.settings = settings;
        zoom.Value = Math.Clamp(15 * settings.DefaultZoomPercent / 100d, zoom.Minimum, zoom.Maximum);
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");

        tablePreview = new ListBox
        {
            ItemsSource = tableRows,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 14,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(18),
            Visibility = Visibility.Collapsed,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        VirtualizingPanel.SetIsVirtualizing(tablePreview, true);
        VirtualizingPanel.SetVirtualizationMode(tablePreview, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(tablePreview, true);

        searchResultList = new ListBox
        {
            ItemsSource = searchResults,
            DisplayMemberPath = nameof(SearchResultOption.DisplayText),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        VirtualizingPanel.SetIsVirtualizing(searchResultList, true);
        searchResultList.SelectionChanged += async (_, _) =>
        {
            if (searchResultList.SelectedItem is SearchResultOption result)
                await NavigateSearchResultAsync(result.Hit);
        };

        slideThumbnailList = new ListBox
        {
            ItemsSource = slideThumbnails,
            DisplayMemberPath = nameof(SlideThumbnailOption.DisplayText),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        VirtualizingPanel.SetIsVirtualizing(slideThumbnailList, true);
        VirtualizingPanel.SetVirtualizationMode(slideThumbnailList, VirtualizationMode.Recycling);
        slideThumbnailList.SelectionChanged += async (_, _) =>
        {
            if (changingSlideThumbnailSelection || slideThumbnailList.SelectedItem is not SlideThumbnailOption option) return;
            await NavigateSlideAsync(option.SlideNumber);
        };

        markdownOutlineList = new ListBox
        {
            ItemsSource = markdownOutline,
            DisplayMemberPath = nameof(MarkdownOutlineOption.DisplayText),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        VirtualizingPanel.SetIsVirtualizing(markdownOutlineList, true);
        VirtualizingPanel.SetVirtualizationMode(markdownOutlineList, VirtualizationMode.Recycling);
        markdownOutlineList.SelectionChanged += (_, _) =>
        {
            if (markdownOutlineList.SelectedItem is not MarkdownOutlineOption option) return;
            NavigateMarkdownHeading(option);
        };

        welcomePanel = new Border
        {
            Child = new ScrollViewer
            {
                Padding = new Thickness(48),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = welcomeContent
            }
        };
        welcomePanel.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");

        recentFilesBox.DisplayMemberPath = nameof(RecentFileEntry.DisplayName);
        recentFilesBox.SelectionChanged += async (_, _) =>
        {
            if (recentFilesBox.SelectedItem is not RecentFileEntry recent) return;
            recentFilesBox.SelectedIndex = -1;
            if (File.Exists(recent.Path))
            {
                await OpenAsync(recent.Path);
            }
            else
            {
                await recentFilesStore.RemoveAsync(recent.Path);
                await RefreshRecentFilesAsync();
                status.Text = "最近文件已不存在，已从列表移除。";
            }
        };

        worksheetBox.SelectionChanged += async (_, _) =>
        {
            if (changingWorksheet || worksheetBox.SelectedIndex < 0) return;
            await SelectWorksheetAsync(worksheetBox.SelectedIndex);
        };

        foreach (var button in new[]
                 {
                     saveButton, saveAsButton, editButton, undoButton, redoButton,
                     cutButton, copyButton, pasteButton, selectAllButton
                 })
        {
            button.Visibility = Visibility.Collapsed;
        }

        saveButton.IsEnabled = false;
        saveButton.Click += async (_, _) => await SaveCurrentDocumentAsync();
        saveAsButton.Click += async (_, _) => await SaveAsCurrentDocumentAsync();
        editButton.Click += (_, _) => ToggleTextEditing();
        undoButton.Click += (_, _) => ApplicationCommands.Undo.Execute(null, textPreview);
        redoButton.Click += (_, _) => ApplicationCommands.Redo.Execute(null, textPreview);
        cutButton.Click += (_, _) => ApplicationCommands.Cut.Execute(null, textPreview);
        copyButton.Click += (_, _) => ApplicationCommands.Copy.Execute(null, textPreview);
        pasteButton.Click += (_, _) => ApplicationCommands.Paste.Execute(null, textPreview);
        selectAllButton.Click += (_, _) => ApplicationCommands.SelectAll.Execute(null, textPreview);
        resetZoomButton.Click += (_, _) => ResetZoom();
        reloadButton.Click += async (_, _) => await ReloadCurrentDocumentAsync();
        documentInfoButton.Click += (_, _) => ToggleDocumentInfo();
        openFolderButton.Click += (_, _) => OpenCurrentFileLocation();
        copyPathButton.Click += (_, _) => CopyCurrentFilePath();
        clearRecentButton.Click += async (_, _) => await ClearRecentFilesAsync();
        previousSearchButton.Click += async (_, _) => await NavigateSearchSelectionAsync(-1);
        nextSearchButton.Click += async (_, _) => await NavigateSearchSelectionAsync(1);
        wrapTextButton.Click += (_, _) => ToggleTextWrapping();
        exportButton.Click += async (_, _) => await ExportCurrentDocumentAsync();
        printButton.Click += (_, _) => PrintCurrentView();
        loadMoreSlidesButton.Click += async (_, _) => await LoadMoreSlideThumbnailsAsync();
        goToLineButton.Click += (_, _) => GoToTextLine();
        markdownPreviewButton.Click += (_, _) => ToggleMarkdownPreview();
        goToLineBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter) GoToTextLine();
        };
        reloadButton.IsEnabled = false;
        documentInfoButton.IsEnabled = false;
        openFolderButton.IsEnabled = false;
        copyPathButton.IsEnabled = false;
        previousSearchButton.IsEnabled = false;
        nextSearchButton.IsEnabled = false;
        exportButton.IsEnabled = false;
        printButton.IsEnabled = false;
        loadMoreSlidesButton.IsEnabled = false;
        goToLineBox.Visibility = Visibility.Collapsed;
        goToLineButton.Visibility = Visibility.Collapsed;
        markdownPreviewButton.Visibility = Visibility.Collapsed;

        statisticsTimer.Tick += (_, _) =>
        {
            statisticsTimer.Stop();
            UpdateTextStatistics();
        };

        textPreview.TextChanged += (_, _) =>
        {
            if (loadingTextPreview || textPreview.IsReadOnly || document is not IEditableTextDocument)
                return;

            isDirty = true;
            UpdateEditingUi();
            ScheduleTextStatistics();
        };
        textPreview.SelectionChanged += (_, _) => ScheduleTextStatistics();

        var slideSurface = new Border
        {
            MaxWidth = 1040,
            MinHeight = 420,
            Margin = new Thickness(24),
            Padding = new Thickness(56, 46, 56, 54),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        slideSurface.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        slideSurface.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        slideSurface.BorderThickness = new Thickness(1);

        var slideContent = new StackPanel();
        slideContent.Children.Add(new Viewbox
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = slideVisualCanvas
        });
        slideContent.Children.Add(slideTitle);
        slideContent.Children.Add(slideBody);
        slideContent.Children.Add(slideEmpty);
        slideSurface.Child = slideContent;
        slideScroll.Content = slideSurface;

        cancelButton.IsEnabled = false;
        cancelButton.Click += (_, _) =>
        {
            if (operationCancellation is not null) operationCancellation.Cancel();
            else loadCancellation?.Cancel();
        };

        loadMoreButton.Visibility = Visibility.Collapsed;
        loadMoreButton.IsEnabled = false;
        loadMoreButton.Click += async (_, _) => await LoadNextTablePageAsync();

        previousSlideButton.Visibility = Visibility.Collapsed;
        nextSlideButton.Visibility = Visibility.Collapsed;
        slideNumberBox.Visibility = Visibility.Collapsed;
        previousSlideButton.Click += async (_, _) => await NavigateSlideAsync(currentSlideNumber - 1);
        nextSlideButton.Click += async (_, _) => await NavigateSlideAsync(currentSlideNumber + 1);
        slideNumberBox.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && int.TryParse(slideNumberBox.Text, out var target))
                await NavigateSlideAsync(target);
        };

        zoom.ValueChanged += (_, _) => ApplyZoom();
        ApplyZoom();

        Content = BuildLayout();

        Loaded += async (_, _) =>
        {
            await RefreshRecentFilesAsync();
            await RefreshWelcomeAsync();
        };
        Unloaded += (_, _) => loadCancellation?.Cancel();
    }

    private FrameworkElement BuildLayout()
    {
        var openButton = CreatePrimaryButton("打开");
        openButton.MinWidth = 78;
        openButton.Click += OpenButton_OnClick;

        var searchButton = CreateSecondaryButton("搜索");
        searchButton.Click += async (_, _) => await SearchCurrentDocumentAsync();
        searchBox.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter) await SearchCurrentDocumentAsync();
        };

        var closeSearchButton = CreateSecondaryButton("��]�����k�w��`             "ExusiAI Viewer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private async Task<bool> ResolvePendingChangesAsync()
    {
        if (!isDirty)
            return true;

        var result = MessageBox.Show(
            "当前文档有未保存的更改。是否先保存？",
            "ExusiAI Viewer",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes => await SaveCurrentDocumentAsync(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    internal bool ConfirmCanClose()
    {
        if (!isDirty)
            return true;

        var result = MessageBox.Show(
            "当前文档有未保存的更改。关闭前是否保存？",
            "ExusiAI Viewer",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
            return false;
        if (result == MessageBoxResult.No)
            return true;
        if (document is not IEditableTextDocument editable || !textPreviewFullyLoaded)
            return false;

        try
        {
            var text = textPreview.Text;
            var path = document.Info.FilePath;
            Task.Run(async () => await editable.SaveTextAsync(text, path)).GetAwaiter().GetResult();
            isDirty = false;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"关闭前保存失败。\n\n{exception.Message}",
                "ExusiAI Viewer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private async Task RefreshRecentFilesAsync()
    {
        recentFilesBox.Visibility = settings.RememberRecentFiles ? Visibility.Visible : Visibility.Collapsed;
        clearRecentButton.Visibility = settings.RememberRecentFiles ? Visibility.Visible : Visibility.Collapsed;
        recentEntries = settings.RememberRecentFiles ? await recentFilesStore.LoadAsync() : [];
        recentFilesBox.ItemsSource = settings.RememberRecentFiles ? recentEntries : null;
    }

    private Task RefreshWelcomeAsync()
    {
        welcomeContent.Children.Clear();
        welcomeContent.Children.Add(new TextBlock
        {
            Text = "打开课堂文档",
            FontSize = 30,
            FontWeight = FontWeights.SemiBold
        });
        welcomeContent.Children.Add(new TextBlock
        {
            Text = "拖入文件，或按 Ctrl+O。TXT/Markdown 支持安全编辑与保存；Office 文档继续以可靠查看和演示为优先。",
            FontSize = 14,
            Margin = new Thickness(0, 8, 0, 24),
            Opacity = 0.72
        });

        var open = CreatePrimaryButton("选择文件");
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Click += async (_, _) => await PickFileAsync();
        welcomeContent.Children.Add(open);

        if (!settings.RememberRecentFiles) return Task.CompletedTask;
        var recent = recentEntries;
        if (recent.Count == 0) return Task.CompletedTask;
        welcomeContent.Children.Add(new TextBlock
        {
            Text = "最近使用",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 30, 0, 10)
        });
        foreach (var item in recent.Take(6))
        {
            var button = CreateSecondaryButton(item.DisplayName);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.ToolTip = item.Path;
            button.Margin = new Thickness(0, 0, 0, 6);
            button.Click += async (_, _) => await OpenAsync(item.Path);
            welcomeContent.Children.Add(button);
        }

        return Task.CompletedTask;
    }

    private async Task CloseDocumentAsync()
    {
        statisticsTimer.Stop();
        fileWatcher?.Dispose();
        fileWatcher = null;
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = null;

        if (tablePages is not null) await tablePages.DisposeAsync();
        tablePages = null;

        if (document is not null) await document.DisposeAsync();
        document = null;
        textPreview.IsReadOnly = true;
        textStatistics.Visibility = Visibility.Collapsed;
        textPreviewFullyLoaded = false;
        isDirty = false;
        externalChangePending = false;
        documentInfoPane.Visibility = Visibility.Collapsed;
        slideNavigationPane.Visibility = Visibility.Collapsed;
        slideThumbnails.Clear();
        markdownOutline.Clear();
        markdownBlocksByLine.Clear();
        markdownParseResult = null;
        isMarkdownDocument = false;
        markdownPreviewMode = false;
        markdownPreview.Visibility = Visibility.Collapsed;
        markdownPreview.Document = new FlowDocument();
        reloadButton.SetResourceReference(Button.BackgroundProperty, "SurfaceAltBrush");
        UpdateEditingUi();
        UpdateDocumentCommandState();
    }

    private static Button CreatePrimaryButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 7, 14, 7),
            MinHeight = 32
        };
        button.SetResourceReference(Button.BackgroundProperty, "AccentBrush");
        button.Foreground = Brushes.White;
        return button;
    }

    private static Button CreateSecondaryButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 7, 12, 7),
            MinHeight = 32
        };
        button.SetResourceReference(Button.BackgroundProperty, "SurfaceAltBrush");
        button.SetResourceReference(Button.ForegroundProperty, "TextPrimaryBrush");
        return button;
    }

    private static Button CreateRibbonTabButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(13, 5, 13, 5),
            MinHeight = 30,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent
        };
        button.SetResourceReference(Button.ForegroundProperty, "TextPrimaryBrush");
        return button;
    }

    private static Border CreateSeparator()
    {
        var separator = new Border
        {
            Width = 1,
            Height = 22,
            Margin = new Thickness(5, 5, 13, 5),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.65
        };
        separator.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        return separator;
    }

    private static void AddCommand(Panel panel, FrameworkElement element)
    {
        element.Margin = new Thickness(0, 0, 8, 4);
        panel.Children.Add(element);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return string.Create(CultureInfo.CurrentCulture, $"{value:0.##} {units[unit]}");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = null;
        operationCancellation?.Cancel();
        statisticsTimer.Stop();
        fileWatcher?.Dispose();
        fileWatcher = null;
        GC.SuppressFinalize(this);
    }

    private sealed record SearchResultOption(ViewerSearchHit Hit)
    {
        public string DisplayText => Hit.Kind switch
        {
            ViewerSearchLocationKind.Slide => $"幻灯片 {Hit.PrimaryIndex:N0}  ·  {Hit.Snippet}",
            ViewerSearchLocationKind.Row => $"第 {Hit.PrimaryIndex:N0} 行 / 第 {Hit.SecondaryIndex:N0} 列  ·  {Hit.Snippet}",
            _ => $"字符 {Hit.PrimaryIndex + 1:N0}  ·  {Hit.Snippet}"
        };
    }

    private sealed record SlideThumbnailOption(int SlideNumber, string Summary)
    {
        public string DisplayText => $"{SlideNumber:N0}  {Summary}";
    }

    private sealed record MarkdownOutlineOption(int SourceLine, int Level, string Title)
    {
        public string DisplayText => $"{new string('　', Math.Max(0, Level - 1))}{Title}";
    }
}
