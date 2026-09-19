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

namespace ExusiAI.Plugin.FileViewer;

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
        Text = "安全只读查看器"
    };

    private readonly TextBlock status = new()
    {
        FontSize = 11,
        Opacity = 0.72,
        Text = "TXT · Markdown · CSV · DOCX · XLSX · PPTX"
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

    private readonly TextBlock slidePreview = new()
    {
        FontSize = 24,
        LineHeight = 38,
        TextWrapping = TextWrapping.Wrap
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

    private readonly Button loadMoreButton = CreateSecondaryButton("加载下一页");
    private readonly Button previousSlideButton = CreateSecondaryButton("上一页");
    private readonly Button nextSlideButton = CreateSecondaryButton("下一页");
    private readonly Button cancelButton = CreateSecondaryButton("取消");
    private readonly TextBox slideNumberBox = new() { Width = 56, ToolTip = "输入幻灯片页码并按 Enter" };
    private readonly TextBox searchBox = new() { Width = 230, ToolTip = "搜索当前文档全部可索引内容" };
    private readonly ComboBox recentFilesBox = new() { Width = 205, ToolTip = "最近打开" };
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

    public FileViewerPage()
    {
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

        slideScroll.Content = new Border
        {
            MaxWidth = 920,
            Margin = new Thickness(34),
            Padding = new Thickness(54, 46, 54, 54),
            CornerRadius = new CornerRadius(8),
            Child = slidePreview
        };
        ((Border)slideScroll.Content).SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        ((Border)slideScroll.Content).SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        ((Border)slideScroll.Content).BorderThickness = new Thickness(1);

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

        zoom.ValueChanged += (_, _) =>
        {
            textPreview.FontSize = zoom.Value;
            tablePreview.FontSize = Math.Max(11, zoom.Value - 1);
            slidePreview.FontSize = Math.Max(18, zoom.Value + 7);
        };

        Content = BuildLayout();

        Loaded += async (_, _) => await RefreshRecentFilesAsync();
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

        var topGrid = new Grid();
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
            Child = new TextBlock { Text = "只读", FontSize = 11, FontWeight = FontWeights.SemiBold }
        };
        safetyChip.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        Grid.SetColumn(safetyChip, 1);
        titleRow.Children.Add(safetyChip);

        var commandRow = new WrapPanel { Orientation = Orientation.Horizontal };
        AddCommand(commandRow, openButton);
        AddCommand(commandRow, recentFilesBox);

        var separator1 = CreateSeparator();
        commandRow.Children.Add(separator1);

        AddCommand(commandRow, searchBox);
        AddCommand(commandRow, searchButton);

        var separator2 = CreateSeparator();
        commandRow.Children.Add(separator2);

        AddCommand(commandRow, previousSlideButton);
        AddCommand(commandRow, slideNumberBox);
        AddCommand(commandRow, nextSlideButton);
        AddCommand(commandRow, loadMoreButton);
        AddCommand(commandRow, cancelButton);

        topGrid.Children.Add(titleRow);
        Grid.SetRow(commandRow, 1);
        topGrid.Children.Add(commandRow);
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

        var contentGrid = new Grid();
        contentGrid.Children.Add(textPreview);
        contentGrid.Children.Add(tablePreview);
        contentGrid.Children.Add(slideScroll);
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

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        workspace.Children.Add(canvas);
        Grid.SetColumn(searchPane, 1);
        workspace.Children.Add(searchPane);

        var bottom = new Border
        {
            MinHeight = 36,
            Padding = new Thickness(16, 6, 16, 6),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        bottom.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        bottom.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var bottomGrid = new Grid();
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition());
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomGrid.Children.Add(status);

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
        Grid.SetColumn(zoomPanel, 1);
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

    private async void OpenButton_OnClick(object sender, RoutedEventArgs e)
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

    private async Task OpenAsync(string filePath)
    {
        await CloseDocumentAsync();

        textPreview.Clear();
        tableRows.Clear();
        slidePreview.Text = string.Empty;
        searchResults.Clear();

        textPreview.Visibility = Visibility.Collapsed;
        tablePreview.Visibility = Visibility.Collapsed;
        slideScroll.Visibility = Visibility.Collapsed;
        searchPane.Visibility = Visibility.Collapsed;

        loadMoreButton.Visibility = Visibility.Collapsed;
        previousSlideButton.Visibility = Visibility.Collapsed;
        nextSlideButton.Visibility = Visibility.Collapsed;
        slideNumberBox.Visibility = Visibility.Collapsed;

        currentSlideNumber = 0;
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

            await recentFilesStore.AddAsync(filePath, loadCancellation.Token);
            await RefreshRecentFilesAsync();

            title.Text = document.Info.DisplayName;
            documentMeta.Text = $"{document.Info.FormatName} · {FormatBytes(document.Info.Length)} · 打开 {timer.ElapsedMilliseconds:N0} ms";
            status.Text = "安全只读 · 不执行宏、脚本、外部链接或嵌入对象";

            switch (document)
            {
                case ITextPreviewDocument text:
                    textPreview.Visibility = Visibility.Visible;
                    await LoadTextPreviewAsync(text, loadCancellation.Token);
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
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or XmlException)
        {
            status.Text = $"文件被安全拒绝：{exception.Message}";
        }
        finally
        {
            cancelButton.IsEnabled = false;
        }
    }

    private async Task LoadTextPreviewAsync(ITextPreviewDocument text, CancellationToken cancellationToken)
    {
        await foreach (var chunk in text.ReadChunksAsync(cancellationToken))
        {
            if (chunk.IsFinal) break;

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
            slidePreview.Text = string.IsNullOrWhiteSpace(slide.Text)
                ? "此页没有可提取的文本内容。"
                : slide.Text;

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

    private async Task SearchCurrentDocumentAsync()
    {
        if (document is null || loadCancellation is null || string.IsNullOrWhiteSpace(searchBox.Text)) return;

        searchResults.Clear();
        searchPane.Visibility = Visibility.Collapsed;
        cancelButton.IsEnabled = true;

        var timer = Stopwatch.StartNew();
        try
        {
            await using var searchDocument = await providers.OpenAsync(
                document.Info.FilePath,
                cancellationToken: loadCancellation.Token);

            var hits = await ViewerSearchService.SearchAsync(
                searchDocument,
                searchBox.Text.Trim(),
                50,
                loadCancellation.Token);

            timer.Stop();

            foreach (var hit in hits) searchResults.Add(new(hit));
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
        }
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

    private async Task RefreshRecentFilesAsync()
    {
        recentFilesBox.ItemsSource = await recentFilesStore.LoadAsync();
    }

    private async Task CloseDocumentAsync()
    {
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = null;

        if (tablePages is not null) await tablePages.DisposeAsync();
        tablePages = null;

        if (document is not null) await document.DisposeAsync();
        document = null;
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
