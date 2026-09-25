using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using ExusiAI.FileViewer.Core;
using Microsoft.Win32;

namespace ExusiAI.FileViewer.Desktop;

internal sealed class FileViewerPage : UserControl, IDisposable
{
    private const int MaximumTextPreviewCharacters = 8 * 1024 * 1024;

    private readonly FileViewerProviderRegistry providers = new(new IFileViewerProvider[]
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

    private readonly ScrollViewer slideScroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Visibility = Visibility.Collapsed
    };

    private readonly ListBox tablePreview;
    private readonly ListBox searchResultList;

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
    private ViewerDocument? document;
    private IAsyncEnumerator<TabularPage>? tablePages;
    private int currentSlideNumber;
    private bool disposed;
    private bool changingWorksheet;
    private bool loadingTextPreview;
    private bool textPreviewFullyLoaded;
    private bool isDirty;
    private bool externalChangePending;
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
        reloadButton.IsEnabled = false;
        documentInfoButton.IsEnabled = false;
        openFolderButton.IsEnabled = false;
        copyPathButton.IsEnabled = false;
        previousSearchButton.IsEnabled = false;
        nextSearchButton.IsEnabled = false;

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
        slideContent.Children.Add(slideTitle);
        slideContent.Children.Add(slideBody);
        slideContent.Children.Add(slideEmpty);
        slideSurface.Child = slideContent;
        slideScroll.Content = slideSurface;

        cancelButton.IsEnabled = false;
        cancelButton.Click += (_, _) => loadCancellation?.Cancel();

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

        var closeSearchButton = CreateSecondaryButton("关闭结果");
        closeSearchButton.Click += (_, _) => searchPane.Visibility = Visibility.Collapsed;

        var top = new Border
        {
            Padding = new Thickness(18, 10, 18, 10),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        top.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        top.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        topBar = top;

        var topGrid = new Grid();
        topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition());
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var identity = new StackPanel();
        identity.Children.Add(title);
        identity.Children.Add(documentMeta);
        titleRow.Children.Add(identity);

        var safetyChip = new Border
        {
            Padding = new Thickness(9, 4, 9, 4),
            CornerRadius = new CornerRadius(12),
            Child = modeChipText
        };
        safetyChip.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        Grid.SetColumn(safetyChip, 1);
        titleRow.Children.Add(safetyChip);

        var fileTabButton = CreateRibbonTabButton("文件");
        var homeTabButton = CreateRibbonTabButton("开始");
        var viewTabButton = CreateRibbonTabButton("查看");

        var tabRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 5)
        };
        tabRow.Children.Add(fileTabButton);
        tabRow.Children.Add(homeTabButton);
        tabRow.Children.Add(viewTabButton);

        var fileCommands = new WrapPanel { Orientation = Orientation.Horizontal };
        AddCommand(fileCommands, openButton);
        AddCommand(fileCommands, recentFilesBox);
        AddCommand(fileCommands, saveButton);
        AddCommand(fileCommands, saveAsButton);
        AddCommand(fileCommands, reloadButton);
        AddCommand(fileCommands, documentInfoButton);
        AddCommand(fileCommands, openFolderButton);
        AddCommand(fileCommands, copyPathButton);
        AddCommand(fileCommands, clearRecentButton);

        var homeCommands = new WrapPanel { Orientation = Orientation.Horizontal };
        AddCommand(homeCommands, editButton);
        AddCommand(homeCommands, undoButton);
        AddCommand(homeCommands, redoButton);
        AddCommand(homeCommands, cutButton);
        AddCommand(homeCommands, copyButton);
        AddCommand(homeCommands, pasteButton);
        AddCommand(homeCommands, selectAllButton);
        AddCommand(homeCommands, searchBox);
        AddCommand(homeCommands, searchButton);
        AddCommand(homeCommands, previousSearchButton);
        AddCommand(homeCommands, nextSearchButton);

        var viewCommands = new WrapPanel { Orientation = Orientation.Horizontal };
        AddCommand(viewCommands, worksheetBox);
        AddCommand(viewCommands, previousSlideButton);
        AddCommand(viewCommands, slideNumberBox);
        AddCommand(viewCommands, nextSlideButton);
        AddCommand(viewCommands, loadMoreButton);
        AddCommand(viewCommands, cancelButton);
        AddCommand(viewCommands, resetZoomButton);
        AddCommand(viewCommands, wrapTextButton);

        var commandHost = new Grid { MinHeight = 36 };
        commandHost.Children.Add(fileCommands);
        commandHost.Children.Add(homeCommands);
        commandHost.Children.Add(viewCommands);

        void SelectRibbonTab(WrapPanel selected, Button selectedTab)
        {
            foreach (var group in new[] { fileCommands, homeCommands, viewCommands })
                group.Visibility = ReferenceEquals(group, selected) ? Visibility.Visible : Visibility.Collapsed;

            foreach (var tab in new[] { fileTabButton, homeTabButton, viewTabButton })
            {
                if (ReferenceEquals(tab, selectedTab))
                    tab.SetResourceReference(Button.BackgroundProperty, "AccentSoftBrush");
                else
                    tab.Background = Brushes.Transparent;
            }
        }

        fileTabButton.Click += (_, _) => SelectRibbonTab(fileCommands, fileTabButton);
        homeTabButton.Click += (_, _) => SelectRibbonTab(homeCommands, homeTabButton);
        viewTabButton.Click += (_, _) => SelectRibbonTab(viewCommands, viewTabButton);
        showHomeCommands = () => SelectRibbonTab(homeCommands, homeTabButton);
        SelectRibbonTab(homeCommands, homeTabButton);

        topGrid.Children.Add(titleRow);
        Grid.SetRow(tabRow, 1);
        topGrid.Children.Add(tabRow);
        Grid.SetRow(commandHost, 2);
        topGrid.Children.Add(commandHost);
        top.Child = topGrid;

        var canvas = new Border
        {
            Margin = new Thickness(16),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };
        canvas.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        canvas.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        documentCanvas = canvas;

        var contentGrid = new Grid();
        contentGrid.Children.Add(textPreview);
        contentGrid.Children.Add(tablePreview);
        contentGrid.Children.Add(slideScroll);
        contentGrid.Children.Add(welcomePanel);
        canvas.Child = contentGrid;

        var resultHeader = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 10) };
        resultHeader.Children.Add(new TextBlock
        {
            Text = "搜索结果",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        DockPanel.SetDock(closeSearchButton, Dock.Right);
        resultHeader.Children.Add(closeSearchButton);

        var resultPanel = new Grid();
        resultPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        resultPanel.RowDefinitions.Add(new RowDefinition());
        resultPanel.Children.Add(resultHeader);
        Grid.SetRow(searchResultList, 1);
        resultPanel.Children.Add(searchResultList);
        searchPane.Child = resultPanel;
        searchPane.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        searchPane.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        searchPane.BorderThickness = new Thickness(1);

        documentInfoPane.Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = documentInfoContent
        };
        documentInfoPane.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        documentInfoPane.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        documentInfoPane.BorderThickness = new Thickness(1);

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        workspace.Children.Add(canvas);
        Grid.SetColumn(searchPane, 1);
        workspace.Children.Add(searchPane);
        Grid.SetColumn(documentInfoPane, 1);
        workspace.Children.Add(documentInfoPane);

        var bottom = new Border
        {
            MinHeight = 36,
            Padding = new Thickness(16, 6, 16, 6),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        bottom.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        bottom.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        bottomBar = bottom;

        var bottomGrid = new Grid();
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition());
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomGrid.Children.Add(status);
        Grid.SetColumn(textStatistics, 1);
        bottomGrid.Children.Add(textStatistics);

        var zoomPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        zoomPanel.Children.Add(new TextBlock
        {
            Text = "缩放",
            FontSize = 11,
            Opacity = 0.72,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        zoomPanel.Children.Add(zoom);
        Grid.SetColumn(zoomPanel, 2);
        bottomGrid.Children.Add(zoomPanel);
        bottom.Child = bottomGrid;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(top);
        Grid.SetRow(workspace, 1);
        root.Children.Add(workspace);
        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);
        return root;
    }

    private async void OpenButton_OnClick(object sender, RoutedEventArgs e) => await PickFileAsync();

    internal async Task PickFileAsync()
    {
        var picker = new OpenFileDialog
        {
            Title = "选择要预览的文件",
            Filter = "支持的文件|*.txt;*.md;*.markdown;*.csv;*.docx;*.xlsx;*.pptx|纯文本|*.txt|Markdown|*.md;*.markdown|CSV|*.csv|Word Open XML|*.docx|Excel Open XML|*.xlsx|PowerPoint Open XML|*.pptx|计划支持的 Office/RTF 文件|*.doc;*.xls;*.ppt;*.rtf|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog() == true) await OpenAsync(picker.FileName);
    }

    internal Task OpenFileAsync(string filePath) => OpenAsync(filePath);
    internal Task SaveCurrentAsync() => SaveCurrentDocumentAsync();
    internal Task SaveAsCurrentAsync() => SaveAsCurrentDocumentAsync();
    internal Task ReloadCurrentAsync() => ReloadCurrentDocumentAsync();
    internal Task NavigateSearchAsync(bool previous) => NavigateSearchSelectionAsync(previous ? -1 : 1);

    internal void FocusSearch()
    {
        if (!HasDocument) return;
        showHomeCommands?.Invoke();
        searchBox.Focus();
        searchBox.SelectAll();
    }

    internal void ZoomBy(double delta) => zoom.Value = Math.Clamp(zoom.Value + delta, zoom.Minimum, zoom.Maximum);
    internal void ResetZoom() => zoom.Value = Math.Clamp(15 * settings.DefaultZoomPercent / 100d, zoom.Minimum, zoom.Maximum);
    internal Task PreviousPageAsync() => NavigateSlideAsync(currentSlideNumber - 1);
    internal Task NextPageAsync() => NavigateSlideAsync(currentSlideNumber + 1);

    internal void SetPresentationMode(bool enabled)
    {
        if (topBar is not null) topBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        if (bottomBar is not null) bottomBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        if (documentCanvas is not null)
        {
            documentCanvas.Margin = enabled ? new Thickness(0) : new Thickness(16);
            documentCanvas.CornerRadius = enabled ? new CornerRadius(0) : new CornerRadius(10);
            documentCanvas.BorderThickness = enabled ? new Thickness(0) : new Thickness(1);
        }
        searchPane.Visibility = Visibility.Collapsed;
        documentInfoPane.Visibility = Visibility.Collapsed;
    }

    private async Task OpenAsync(string filePath, bool skipPendingPrompt = false)
    {
        if (!skipPendingPrompt && !await ResolvePendingChangesAsync())
            return;

        await CloseDocumentAsync();

        loadingTextPreview = true;
        textPreview.Clear();
        loadingTextPreview = false;
        textPreview.IsReadOnly = true;
        textPreviewFullyLoaded = false;
        isDirty = false;
        tableRows.Clear();
        slideTitle.Text = string.Empty;
        slideTitle.Visibility = Visibility.Collapsed;
        slideBody.Text = string.Empty;
        slideEmpty.Visibility = Visibility.Collapsed;
        searchResults.Clear();

        textPreview.Visibility = Visibility.Collapsed;
        tablePreview.Visibility = Visibility.Collapsed;
        slideScroll.Visibility = Visibility.Collapsed;
        searchPane.Visibility = Visibility.Collapsed;
        documentInfoPane.Visibility = Visibility.Collapsed;

        loadMoreButton.Visibility = Visibility.Collapsed;
        previousSlideButton.Visibility = Visibility.Collapsed;
        nextSlideButton.Visibility = Visibility.Collapsed;
        slideNumberBox.Visibility = Visibility.Collapsed;
        worksheetBox.Visibility = Visibility.Collapsed;

        currentSlideNumber = 0;
        welcomePanel.Visibility = Visibility.Collapsed;
        loadCancellation = new CancellationTokenSource();
        cancelButton.IsEnabled = true;
        title.Text = Path.GetFileName(filePath);
        documentMeta.Text = "正在安全打开…";
        status.Text = "正在读取文件…";

        try
        {
            var timer = Stopwatch.StartNew();
            document = await providers.OpenAsync(filePath, cancellationToken: loadCancellation.Token);
            timer.Stop();

            if (settings.RememberRecentFiles)
            {
                await recentFilesStore.AddAsync(filePath, loadCancellation.Token);
                await RefreshRecentFilesAsync();
            }

            title.Text = document.Info.DisplayName;
            lastKnownWriteTimeUtc = File.GetLastWriteTimeUtc(document.Info.FilePath);
            externalChangePending = false;
            StartWatchingCurrentFile();
            DocumentOpened?.Invoke(this, document.Info.FilePath);
            documentMeta.Text = $"{document.Info.FormatName} · {FormatBytes(document.Info.Length)} · 打开 {timer.ElapsedMilliseconds:N0} ms";
            status.Text = "安全只读 · 不执行宏、脚本、外部链接或嵌入对象";

            switch (document)
            {
                case ITextPreviewDocument text:
                    textPreview.Visibility = Visibility.Visible;
                    loadingTextPreview = true;
                    try
                    {
                        textPreviewFullyLoaded = await LoadTextPreviewAsync(text, loadCancellation.Token);
                    }
                    finally
                    {
                        loadingTextPreview = false;
                    }

                    if (document is IEditableTextDocument && textPreviewFullyLoaded)
                        status.Text = "TXT/Markdown 可编辑 · Ctrl+S 保存 · 保存采用同目录临时文件与原子替换";
                    else if (document is IEditableTextDocument)
                        status.Text = "文件超过 8 MiB 界面缓存，已保持只读以防止截断保存。";
                    UpdateEditingUi();
                    UpdateTextStatistics();
                    break;

                case IWorkbookPreviewDocument workbook:
                    tablePreview.Visibility = Visibility.Visible;
                    loadMoreButton.Visibility = Visibility.Visible;
                    changingWorksheet = true;
                    worksheetBox.ItemsSource = workbook.WorksheetNames;
                    worksheetBox.SelectedIndex = workbook.ActiveWorksheetIndex;
                    worksheetBox.Visibility = Visibility.Visible;
                    changingWorksheet = false;
                    tablePages = workbook.ReadPagesAsync(loadCancellation.Token).GetAsyncEnumerator(loadCancellation.Token);
                    await LoadNextTablePageAsync();
                    break;

                case ITabularPreviewDocument table:
                    tablePreview.Visibility = Visibility.Visible;
                    loadMoreButton.Visibility = Visibility.Visible;
                    tablePages = table.ReadPagesAsync(loadCancellation.Token).GetAsyncEnumerator(loadCancellation.Token);
                    await LoadNextTablePageAsync();
                    break;

                case ISlidePreviewDocument:
                    slideScroll.Visibility = Visibility.Visible;
                    previousSlideButton.Visibility = Visibility.Visible;
                    nextSlideButton.Visibility = Visibility.Visible;
                    slideNumberBox.Visibility = Visibility.Visible;
                    await NavigateSlideAsync(1);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            status.Text = "已取消加载。";
        }
        catch (UnsupportedFileFormatException)
        {
            status.Text = "此格式尚未启用可靠 Provider。DOC、XLS、PPT、RTF 当前明确为未实现。";
            welcomePanel.Visibility = Visibility.Visible;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or XmlException)
        {
            status.Text = $"文件被安全拒绝：{exception.Message}";
            welcomePanel.Visibility = Visibility.Visible;
        }
        finally
        {
            loadingTextPreview = false;
            cancelButton.IsEnabled = false;
            UpdateEditingUi();
            UpdateDocumentCommandState();
        }
    }

    private async Task<bool> LoadTextPreviewAsync(ITextPreviewDocument text, CancellationToken cancellationToken)
    {
        var complete = false;
        await foreach (var chunk in text.ReadChunksAsync(cancellationToken))
        {
            if (chunk.IsFinal)
            {
                complete = true;
                break;
            }

            var remaining = MaximumTextPreviewCharacters - textPreview.Text.Length;
            if (remaining <= 0)
            {
                status.Text = "已达到 8 MiB 界面缓存上限，剩余内容未载入。";
                break;
            }

            textPreview.AppendText(chunk.Text.Length <= remaining ? chunk.Text : chunk.Text[..remaining]);
            if (chunk.Text.Length > remaining)
            {
                status.Text = "已达到 8 MiB 界面缓存上限，剩余内容未载入。";
                break;
            }

            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        }

        textPreview.ScrollToHome();
        return complete;
    }

    private async Task LoadNextTablePageAsync()
    {
        if (tablePages is null) return;

        loadMoreButton.IsEnabled = false;
        cancelButton.IsEnabled = true;
        try
        {
            if (!await tablePages.MoveNextAsync())
            {
                loadMoreButton.Visibility = Visibility.Collapsed;
                return;
            }

            var page = tablePages.Current;
            foreach (var row in page.Rows) tableRows.Add(string.Join("  │  ", row));

            status.Text = $"已加载 {tableRows.Count:N0} 行 · 分页读取 · 回收式虚拟化";
            loadMoreButton.Visibility = page.IsFinal ? Visibility.Collapsed : Visibility.Visible;
            loadMoreButton.IsEnabled = !page.IsFinal;
        }
        catch (OperationCanceledException)
        {
            status.Text = "已取消加载。";
        }
        catch (InvalidDataException exception)
        {
            status.Text = $"表格被安全拒绝：{exception.Message}";
        }
        finally
        {
            cancelButton.IsEnabled = false;
            UpdateDocumentCommandState();
        }
    }

    private async Task SelectWorksheetAsync(int index)
    {
        if (document is not IWorkbookPreviewDocument workbook || loadCancellation is null) return;
        changingWorksheet = true;
        try
        {
            if (tablePages is not null) await tablePages.DisposeAsync();
            tablePages = null;
            tableRows.Clear();
            workbook.SelectWorksheet(index);
            tablePages = workbook.ReadPagesAsync(loadCancellation.Token).GetAsyncEnumerator(loadCancellation.Token);
            loadMoreButton.Visibility = Visibility.Visible;
            await LoadNextTablePageAsync();
            documentMeta.Text = $"XLSX · {workbook.WorksheetNames[index]} · 工作表 {index + 1:N0} / {workbook.WorksheetNames.Count:N0}";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            status.Text = $"无法切换工作表：{exception.Message}";
        }
        finally
        {
            changingWorksheet = false;
        }
    }

    private async Task NavigateSlideAsync(int slideNumber)
    {
        if (document is not ISlidePreviewDocument slides || loadCancellation is null) return;

        if (slideNumber < 1 || slideNumber > slides.SlideCount)
        {
            status.Text = $"页码范围为 1–{slides.SlideCount:N0}。";
            slideNumberBox.Text = currentSlideNumber > 0
                ? currentSlideNumber.ToString(CultureInfo.CurrentCulture)
                : string.Empty;
            return;
        }

        previousSlideButton.IsEnabled = false;
        nextSlideButton.IsEnabled = false;
        slideNumberBox.IsEnabled = false;
        cancelButton.IsEnabled = true;

        try
        {
            var timer = Stopwatch.StartNew();
            var slide = await slides.ReadSlideAsync(slideNumber, loadCancellation.Token);
            timer.Stop();

            currentSlideNumber = slide.SlideNumber;
            slideNumberBox.Text = slide.SlideNumber.ToString(CultureInfo.CurrentCulture);
            RenderSlideText(slide.Text);

            documentMeta.Text = $"{document.Info.FormatName} · 幻灯片 {slide.SlideNumber:N0} / {slides.SlideCount:N0}";
            status.Text = $"切页 {timer.ElapsedMilliseconds:N0} ms · 相邻页后台预热 · 有界缓存";
            _ = PrewarmAdjacentSlidesAsync(slides, slide.SlideNumber, loadCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            status.Text = "已取消加载。";
        }
        catch (InvalidDataException exception)
        {
            status.Text = $"PPTX 被安全拒绝：{exception.Message}";
        }
        finally
        {
            cancelButton.IsEnabled = false;
            slideNumberBox.IsEnabled = true;
            previousSlideButton.IsEnabled = currentSlideNumber > 1;
            nextSlideButton.IsEnabled = currentSlideNumber > 0 && currentSlideNumber < slides.SlideCount;
        }
    }

    private void ApplyZoom()
    {
        textPreview.FontSize = zoom.Value;
        tablePreview.FontSize = Math.Max(11, zoom.Value - 1);
        slideTitle.FontSize = Math.Max(24, zoom.Value + 15);
        slideTitle.LineHeight = slideTitle.FontSize * 1.3;
        slideBody.FontSize = Math.Max(16, zoom.Value + 5);
        slideBody.LineHeight = slideBody.FontSize * 1.55;
    }

    private void RenderSlideText(string text)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        slideTitle.Text = string.Empty;
        slideTitle.Visibility = Visibility.Collapsed;
        slideBody.Text = string.Empty;

        if (lines.Length == 0)
        {
            slideEmpty.Visibility = Visibility.Visible;
            return;
        }

        slideEmpty.Visibility = Visibility.Collapsed;
        if (lines[0].Length <= 90)
        {
            slideTitle.Text = lines[0];
            slideTitle.Visibility = Visibility.Visible;
            slideBody.Text = string.Join(Environment.NewLine + Environment.NewLine, lines.Skip(1));
        }
        else
        {
            slideBody.Text = string.Join(Environment.NewLine + Environment.NewLine, lines);
        }
    }

    private async Task SearchCurrentDocumentAsync()
    {
        if (document is null || loadCancellation is null || string.IsNullOrWhiteSpace(searchBox.Text)) return;

        searchResults.Clear();
        searchPane.Visibility = Visibility.Collapsed;
        documentInfoPane.Visibility = Visibility.Collapsed;
        cancelButton.IsEnabled = true;

        var timer = Stopwatch.StartNew();
        try
        {
            if (document is IEditableTextDocument && textPreviewFullyLoaded)
            {
                SearchLoadedText(searchBox.Text.Trim());
                timer.Stop();
                searchPane.Visibility = searchResults.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                status.Text = searchResults.Count == 0
                    ? $"搜索 {timer.ElapsedMilliseconds:N0} ms · 未找到匹配项"
                    : $"搜索 {timer.ElapsedMilliseconds:N0} ms · {searchResults.Count:N0} 个结果";
                return;
            }

            await using var searchDocument = await providers.OpenAsync(
                document.Info.FilePath,
                cancellationToken: loadCancellation.Token);
            if (document is IWorkbookPreviewDocument currentWorkbook &&
                searchDocument is IWorkbookPreviewDocument searchWorkbook)
            {
                searchWorkbook.SelectWorksheet(currentWorkbook.ActiveWorksheetIndex);
            }

            var hits = await ViewerSearchService.SearchAsync(
                searchDocument,
                searchBox.Text.Trim(),
                50,
                loadCancellation.Token);

            timer.Stop();

            foreach (var hit in hits) searchResults.Add(new(hit));
            if (searchResults.Count > 0) searchResultList.SelectedIndex = 0;
            searchPane.Visibility = hits.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            status.Text = hits.Length == 0
                ? $"搜索 {timer.ElapsedMilliseconds:N0} ms · 未找到匹配项"
                : $"搜索 {timer.ElapsedMilliseconds:N0} ms · {hits.Length:N0} 个结果";
        }
        catch (OperationCanceledException)
        {
            status.Text = "已取消搜索。";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or XmlException)
        {
            status.Text = $"搜索被安全终止：{exception.Message}";
        }
        finally
        {
            cancelButton.IsEnabled = false;
            UpdateDocumentCommandState();
        }
    }

    private async Task NavigateSearchSelectionAsync(int direction)
    {
        if (searchResults.Count == 0)
        {
            await SearchCurrentDocumentAsync();
            if (searchResults.Count == 0) return;
        }

        var current = searchResultList.SelectedIndex;
        var next = current < 0
            ? direction < 0 ? searchResults.Count - 1 : 0
            : (current + direction + searchResults.Count) % searchResults.Count;
        searchResultList.SelectedIndex = next;
        searchResultList.ScrollIntoView(searchResultList.SelectedItem);
        searchPane.Visibility = Visibility.Visible;
        status.Text = $"搜索结果 {next + 1:N0} / {searchResults.Count:N0}";
    }

    private void ToggleTextWrapping()
    {
        textPreview.TextWrapping = textPreview.TextWrapping == TextWrapping.NoWrap
            ? TextWrapping.Wrap
            : TextWrapping.NoWrap;
        textPreview.HorizontalScrollBarVisibility = textPreview.TextWrapping == TextWrapping.Wrap
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
        wrapTextButton.Content = textPreview.TextWrapping == TextWrapping.Wrap ? "取消换行" : "自动换行";
        status.Text = textPreview.TextWrapping == TextWrapping.Wrap ? "已启用自动换行。" : "已关闭自动换行。";
    }

    private void ScheduleTextStatistics()
    {
        if (textPreview.Visibility != Visibility.Visible) return;
        statisticsTimer.Stop();
        statisticsTimer.Start();
    }

    private void UpdateTextStatistics()
    {
        if (textPreview.Visibility != Visibility.Visible) return;

        var statistics = TextDocumentStatistics.Calculate(textPreview.Text);
        var caret = Math.Clamp(textPreview.CaretIndex, 0, textPreview.Text.Length);
        var line = textPreview.GetLineIndexFromCharacterIndex(caret);
        var lineStart = line >= 0 ? textPreview.GetCharacterIndexFromLineIndex(line) : 0;
        var column = Math.Max(0, caret - lineStart);
        textStatistics.Text = $"{statistics.Lines:N0} 行 · {statistics.Words:N0} 词 · {statistics.Characters:N0} 字符 · 第 {line + 1:N0} 行，第 {column + 1:N0} 列";
        textStatistics.Visibility = Visibility.Visible;
    }

    private async Task ReloadCurrentDocumentAsync()
    {
        if (document is null) return;
        if (!await ResolvePendingChangesAsync()) return;

        var path = document.Info.FilePath;
        if (!File.Exists(path))
        {
            status.Text = "源文件已被移动或删除，无法重新加载。";
            return;
        }

        await OpenAsync(path, skipPendingPrompt: true);
        status.Text = "已从磁盘重新加载。";
    }

    private void ToggleDocumentInfo()
    {
        if (document is null) return;
        if (documentInfoPane.Visibility == Visibility.Visible)
        {
            documentInfoPane.Visibility = Visibility.Collapsed;
            return;
        }

        searchPane.Visibility = Visibility.Collapsed;
        BuildDocumentInfo();
        documentInfoPane.Visibility = Visibility.Visible;
    }

    private void BuildDocumentInfo()
    {
        documentInfoContent.Children.Clear();
        if (document is null) return;

        documentInfoContent.Children.Add(new TextBlock
        {
            Text = "文档信息",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var file = new FileInfo(document.Info.FilePath);
        AddInfoRow("名称", document.Info.DisplayName);
        AddInfoRow("类型", document.Info.FormatName);
        AddInfoRow("大小", FormatBytes(document.Info.Length));
        AddInfoRow("修改时间", file.Exists ? file.LastWriteTime.ToString("g", CultureInfo.CurrentCulture) : "文件已不存在");
        AddInfoRow("模式", document is IEditableTextDocument && textPreviewFullyLoaded ? "可编辑文本" : "安全只读");
        AddInfoRow("路径", document.Info.FilePath);

        if (!document.Info.Warnings.IsDefaultOrEmpty)
        {
            documentInfoContent.Children.Add(new TextBlock
            {
                Text = "安全与兼容性",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 18, 0, 8)
            });
            foreach (var warning in document.Info.Warnings)
            {
                documentInfoContent.Children.Add(new TextBlock
                {
                    Text = "• " + warning,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.76,
                    Margin = new Thickness(0, 0, 0, 7)
                });
            }
        }

        void AddInfoRow(string label, string value)
        {
            documentInfoContent.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Opacity = 0.62,
                Margin = new Thickness(0, 0, 0, 2)
            });
            documentInfoContent.Children.Add(new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
        }
    }

    private void OpenCurrentFileLocation()
    {
        if (document is null) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{document.Info.FilePath}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            status.Text = $"无法打开文件位置：{exception.Message}";
        }
    }

    private void CopyCurrentFilePath()
    {
        if (document is null) return;
        try
        {
            Clipboard.SetText(document.Info.FilePath);
            status.Text = "已复制完整路径。";
        }
        catch (System.Runtime.InteropServices.ExternalException exception)
        {
            status.Text = $"复制路径失败：{exception.Message}";
        }
    }

    private async Task ClearRecentFilesAsync()
    {
        await recentFilesStore.ClearAsync();
        await RefreshRecentFilesAsync();
        await RefreshWelcomeAsync();
        status.Text = "已清除最近使用记录；不会删除任何文档。";
    }

    private void UpdateDocumentCommandState()
    {
        var hasDocument = document is not null;
        reloadButton.IsEnabled = hasDocument;
        documentInfoButton.IsEnabled = hasDocument;
        openFolderButton.IsEnabled = hasDocument;
        copyPathButton.IsEnabled = hasDocument;
        wrapTextButton.IsEnabled = document is ITextPreviewDocument;
        previousSearchButton.IsEnabled = searchResults.Count > 0;
        nextSearchButton.IsEnabled = searchResults.Count > 0;
    }

    private void StartWatchingCurrentFile()
    {
        fileWatcher?.Dispose();
        fileWatcher = null;
        if (document is null) return;

        var directory = Path.GetDirectoryName(document.Info.FilePath);
        var fileName = Path.GetFileName(document.Info.FilePath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName)) return;

        try
        {
            fileWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            fileWatcher.Changed += CurrentFile_OnDiskChanged;
            fileWatcher.Deleted += CurrentFile_OnDiskChanged;
            fileWatcher.Renamed += CurrentFile_OnDiskChanged;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or PlatformNotSupportedException)
        {
            fileWatcher?.Dispose();
            fileWatcher = null;
        }
    }

    private void CurrentFile_OnDiskChanged(object sender, FileSystemEventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (document is null) return;
            var currentWriteTime = File.Exists(document.Info.FilePath)
                ? File.GetLastWriteTimeUtc(document.Info.FilePath)
                : DateTime.MinValue;
            if (currentWriteTime == lastKnownWriteTimeUtc) return;

            externalChangePending = true;
            reloadButton.SetResourceReference(Button.BackgroundProperty, "AccentSoftBrush");
            status.Text = isDirty
                ? "磁盘上的文件已更改；当前编辑尚未保存，请保存或重新加载后处理冲突。"
                : "磁盘上的文件已更改，点击“重新加载”获取最新内容。";
        });
    }

    private void SearchLoadedText(string query)
    {
        var source = textPreview.Text;
        var searchFrom = 0;
        while (searchResults.Count < 50 && searchFrom <= source.Length - query.Length)
        {
            var index = source.IndexOf(query, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (index < 0) break;

            searchResults.Add(new(new(
                ViewerSearchLocationKind.Text,
                index,
                0,
                CreateSearchSnippet(source, index, query.Length))));
            searchFrom = index + Math.Max(1, query.Length);
        }

        if (searchResults.Count > 0) searchResultList.SelectedIndex = 0;
    }

    private static string CreateSearchSnippet(string text, int matchIndex, int matchLength)
    {
        const int context = 28;
        var start = Math.Max(0, matchIndex - context);
        var end = Math.Min(text.Length, matchIndex + matchLength + context);
        var snippet = text[start..end]
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
        if (start > 0) snippet = "…" + snippet;
        if (end < text.Length) snippet += "…";
        return snippet;
    }

    private async Task NavigateSearchResultAsync(ViewerSearchHit hit)
    {
        switch (hit.Kind)
        {
            case ViewerSearchLocationKind.Slide:
                await NavigateSlideAsync(checked((int)hit.PrimaryIndex));
                break;

            case ViewerSearchLocationKind.Text:
                if (textPreview.Visibility != Visibility.Visible) return;
                var index = checked((int)Math.Min(hit.PrimaryIndex, int.MaxValue));
                if (index < textPreview.Text.Length)
                {
                    var length = Math.Min(searchBox.Text.Length, textPreview.Text.Length - index);
                    textPreview.Select(index, length);
                    textPreview.Focus();
                    textPreview.ScrollToLine(textPreview.GetLineIndexFromCharacterIndex(index));
                }
                else
                {
                    status.Text = "该结果位于当前 8 MiB 界面缓存之外。";
                }
                break;

            case ViewerSearchLocationKind.Row:
                await NavigateTableRowAsync(hit.PrimaryIndex);
                break;
        }
    }

    private async Task NavigateTableRowAsync(long oneBasedRow)
    {
        if (document is not ITabularPreviewDocument table || loadCancellation is null) return;

        if (tablePages is not null) await tablePages.DisposeAsync();
        tablePages = table.ReadPagesAsync(loadCancellation.Token).GetAsyncEnumerator(loadCancellation.Token);
        tableRows.Clear();

        while (await tablePages.MoveNextAsync())
        {
            var page = tablePages.Current;
            var first = page.StartRow + 1;
            var last = page.StartRow + page.Rows.Length;
            if (oneBasedRow < first || oneBasedRow > last) continue;

            foreach (var row in page.Rows) tableRows.Add(string.Join("  │  ", row));
            status.Text = $"第 {oneBasedRow:N0} 行 · 当前页 {first:N0}–{last:N0}";
            loadMoreButton.Visibility = page.IsFinal ? Visibility.Collapsed : Visibility.Visible;
            loadMoreButton.IsEnabled = !page.IsFinal;
            return;
        }

        status.Text = $"无法定位第 {oneBasedRow:N0} 行。";
    }

    private async Task PrewarmAdjacentSlidesAsync(
        ISlidePreviewDocument slides,
        int slideNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var neighbor in new[] { slideNumber - 1, slideNumber + 1 }
                         .Where(number => number >= 1 && number <= slides.SlideCount)
                         .Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = await slides.ReadSlideAsync(neighbor, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (InvalidDataException) { }
    }

    private void ToggleTextEditing()
    {
        if (document is not IEditableTextDocument || !textPreviewFullyLoaded)
            return;

        textPreview.IsReadOnly = !textPreview.IsReadOnly;
        UpdateEditingUi();
        if (!textPreview.IsReadOnly)
        {
            textPreview.Focus();
            status.Text = "编辑模式 · Ctrl+S 保存 · Ctrl+Z/Ctrl+Y 撤销与重做";
        }
        else
        {
            status.Text = isDirty ? "已退出编辑模式 · 仍有未保存更改" : "已退出编辑模式";
        }
    }

    private void UpdateEditingUi()
    {
        var editable = document is IEditableTextDocument && textPreviewFullyLoaded;
        foreach (var button in new[]
                 {
                     saveButton, saveAsButton, editButton, undoButton, redoButton,
                     cutButton, copyButton, pasteButton, selectAllButton
                 })
        {
            button.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        }

        saveButton.IsEnabled = editable && isDirty;
        saveAsButton.IsEnabled = editable;
        editButton.IsEnabled = editable;
        editButton.Content = textPreview.IsReadOnly ? "启用编辑" : "结束编辑";

        var editing = editable && !textPreview.IsReadOnly;
        undoButton.IsEnabled = editing;
        redoButton.IsEnabled = editing;
        cutButton.IsEnabled = editing;
        pasteButton.IsEnabled = editing;
        copyButton.IsEnabled = editable;
        selectAllButton.IsEnabled = editable;

        modeChipText.Text = editable
            ? isDirty ? "已修改" : editing ? "编辑中" : "可编辑"
            : "只读";

        if (document is not null)
            title.Text = document.Info.DisplayName + (isDirty ? " *" : string.Empty);
    }

    private async Task<bool> SaveCurrentDocumentAsync()
    {
        if (document is not IEditableTextDocument editable || !textPreviewFullyLoaded)
            return false;

        if (externalChangePending)
        {
            var overwrite = MessageBox.Show(
                "磁盘上的文件已被其他程序修改。继续保存会覆盖外部更改。是否仍要保存？",
                "检测到外部更改",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (overwrite != MessageBoxResult.Yes) return false;
        }

        try
        {
            await editable.SaveTextAsync(textPreview.Text, document.Info.FilePath);
            lastKnownWriteTimeUtc = File.GetLastWriteTimeUtc(document.Info.FilePath);
            externalChangePending = false;
            reloadButton.SetResourceReference(Button.BackgroundProperty, "SurfaceAltBrush");
            isDirty = false;
            UpdateEditingUi();
            status.Text = $"已保存 · {DateTime.Now:T}";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            status.Text = $"保存失败：{exception.Message}";
            MessageBox.Show(
                $"无法保存文档。\n\n{exception.Message}",
                "ExusiAI Viewer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private async Task<bool> SaveAsCurrentDocumentAsync()
    {
        if (document is not IEditableTextDocument editable || !textPreviewFullyLoaded)
            return false;

        var extension = Path.GetExtension(document.Info.FilePath);
        var dialog = new SaveFileDialog
        {
            Title = "另存为",
            FileName = Path.GetFileName(document.Info.FilePath),
            InitialDirectory = Path.GetDirectoryName(document.Info.FilePath),
            Filter = extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                ? "纯文本|*.txt|Markdown|*.md;*.markdown"
                : "Markdown|*.md;*.markdown|纯文本|*.txt",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true)
            return false;

        try
        {
            await editable.SaveTextAsync(textPreview.Text, dialog.FileName);
            isDirty = false;
            await OpenAsync(dialog.FileName, skipPendingPrompt: true);
            status.Text = "已另存并切换到新文件。";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            status.Text = $"另存为失败：{exception.Message}";
            MessageBox.Show(
                $"无法另存文档。\n\n{exception.Message}",
                "ExusiAI Viewer",
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
        recentFilesBox.ItemsSource = settings.RememberRecentFiles ? await recentFilesStore.LoadAsync() : null;
    }

    private async Task RefreshWelcomeAsync()
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

        if (!settings.RememberRecentFiles) return;
        var recent = await recentFilesStore.LoadAsync();
        if (recent.Count == 0) return;
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
}
