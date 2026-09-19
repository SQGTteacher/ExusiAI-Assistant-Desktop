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
    private readonly TextBlock title = new() { FontSize = 22, FontWeight = FontWeights.SemiBold, Text = "尚未打开文件" };
    private readonly TextBlock status = new() { Opacity = 0.68, Text = "支持 TXT、Markdown、CSV、DOCX、XLSX 与 PPTX 的安全只读预览" };
    private readonly TextBox textPreview = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        AcceptsTab = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 15,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Visibility = Visibility.Collapsed
    };
    private readonly ObservableCollection<string> tableRows = [];
    private readonly ObservableCollection<SearchResultOption> searchResults = [];
    private readonly ListBox tablePreview;
    private readonly Button loadMoreButton = new() { Content = "加载下一页", IsEnabled = false, Visibility = Visibility.Collapsed };
    private readonly Button previousSlideButton = new() { Content = "上一页", IsEnabled = false, Visibility = Visibility.Collapsed };
    private readonly Button nextSlideButton = new() { Content = "下一页", IsEnabled = false, Visibility = Visibility.Collapsed };
    private readonly TextBox slideNumberBox = new() { Width = 58, Visibility = Visibility.Collapsed, ToolTip = "输入幻灯片页码并按 Enter" };
    private readonly Button cancelButton = new() { Content = "取消", IsEnabled = false };
    private readonly TextBox searchBox = new() { MinWidth = 180, ToolTip = "搜索当前文档全部可索引内容" };
    private readonly ComboBox searchResultBox = new() { Width = 280, Visibility = Visibility.Collapsed, IsTextSearchEnabled = false };
    private readonly ComboBox recentFilesBox = new() { Width = 220, ToolTip = "最近打开的文件" };
    private CancellationTokenSource? loadCancellation;
    private ViewerDocument? document;
    private IAsyncEnumerator<TabularPage>? tablePages;
    private int currentSlideNumber;
    private bool disposed;

    public FileViewerPage()
    {
        tablePreview = new ListBox
        {
            ItemsSource = tableRows,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Visibility = Visibility.Collapsed,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        VirtualizingPanel.SetIsVirtualizing(tablePreview, true);
        VirtualizingPanel.SetVirtualizationMode(tablePreview, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(tablePreview, true);

        searchResultBox.ItemsSource = searchResults;
        searchResultBox.DisplayMemberPath = nameof(SearchResultOption.DisplayText);
        recentFilesBox.DisplayMemberPath = nameof(RecentFileEntry.DisplayName);

        var openButton = new Button { Content = "打开文件", MinWidth = 96 };
        openButton.Click += OpenButton_OnClick;
        cancelButton.Click += (_, _) => loadCancellation?.Cancel();
        loadMoreButton.Click += async (_, _) => await LoadNextTablePageAsync();
        previousSlideButton.Click += async (_, _) => await NavigateSlideAsync(currentSlideNumber - 1);
        nextSlideButton.Click += async (_, _) => await NavigateSlideAsync(currentSlideNumber + 1);
        slideNumberBox.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && int.TryParse(slideNumberBox.Text, out var target))
                await NavigateSlideAsync(target);
        };

        var searchButton = new Button { Content = "搜索全文" };
        searchButton.Click += async (_, _) => await SearchCurrentDocumentAsync();
        searchBox.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter) await SearchCurrentDocumentAsync();
        };
        searchResultBox.SelectionChanged += async (_, _) =>
        {
            if (searchResultBox.SelectedItem is SearchResultOption result)
                await NavigateSearchResultAsync(result.Hit);
        };
        recentFilesBox.SelectionChanged += async (_, _) =>
        {
            if (recentFilesBox.SelectedItem is not RecentFileEntry recent) return;
            recentFilesBox.SelectedIndex = -1;
            if (File.Exists(recent.Path)) await OpenAsync(recent.Path);
            else
            {
                await recentFilesStore.RemoveAsync(recent.Path);
                await RefreshRecentFilesAsync();
                status.Text = "最近文件已不存在，已从列表移除。";
            }
        };

        var zoom = new Slider { Minimum = 11, Maximum = 28, Value = 15, Width = 120, TickFrequency = 1, IsSnapToTickEnabled = true };
        zoom.ValueChanged += (_, _) =>
        {
            textPreview.FontSize = zoom.Value;
            tablePreview.FontSize = Math.Max(11, zoom.Value - 1);
        };

        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var element in new FrameworkElement[]
        {
            openButton, recentFilesBox, cancelButton, searchBox, searchButton, searchResultBox,
            loadMoreButton, previousSlideButton, slideNumberBox, nextSlideButton,
            new TextBlock { Text = "缩放", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) }, zoom
        })
        {
            element.Margin = element.Margin == default ? new Thickness(0, 0, 8, 8) : element.Margin;
            toolbar.Children.Add(element);
        }

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(title);
        header.Children.Add(status);
        header.Children.Add(toolbar);

        var content = new Grid();
        content.Children.Add(textPreview);
        content.Children.Add(tablePreview);

        var layout = new Grid { Margin = new Thickness(24) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(header);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);
        Content = layout;

        Loaded += async (_, _) => await RefreshRecentFilesAsync();
        Unloaded += (_, _) => loadCancellation?.Cancel();
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
        if (picker.ShowDialog() != true) return;
        await OpenAsync(picker.FileName);
    }

    private async Task OpenAsync(string filePath)
    {
        await CloseDocumentAsync();
        textPreview.Clear();
        tableRows.Clear();
        searchResults.Clear();
        searchResultBox.Visibility = Visibility.Collapsed;
        textPreview.Visibility = Visibility.Collapsed;
        tablePreview.Visibility = Visibility.Collapsed;
        loadMoreButton.Visibility = Visibility.Collapsed;
        previousSlideButton.Visibility = Visibility.Collapsed;
        nextSlideButton.Visibility = Visibility.Collapsed;
        slideNumberBox.Visibility = Visibility.Collapsed;
        currentSlideNumber = 0;
        loadCancellation = new CancellationTokenSource();
        cancelButton.IsEnabled = true;
        title.Text = Path.GetFileName(filePath);
        status.Text = "正在安全打开…";

        try
        {
            var timer = Stopwatch.StartNew();
            document = await providers.OpenAsync(filePath, cancellationToken: loadCancellation.Token);
            timer.Stop();
            await recentFilesStore.AddAsync(filePath, loadCancellation.Token);
            await RefreshRecentFilesAsync();

            title.Text = $"{document.Info.DisplayName} · {document.Info.FormatName}";
            status.Text = $"只读 · {FormatBytes(document.Info.Length)} · 打开 {timer.ElapsedMilliseconds:N0} ms · 不执行宏、脚本或外部内容";

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
                case ISlidePreviewDocument slides:
                    tablePreview.Visibility = Visibility.Visible;
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
            status.Text = "此格式尚未启用可靠 Provider。DOC、XLS、PPT、RTF 当前明确为未实现，而不是低保真冒充支持。";
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
                status.Text += " · 已达到 8 MiB 界面缓存上限，剩余内容未载入";
                break;
            }

            textPreview.AppendText(chunk.Text.Length <= remaining ? chunk.Text : chunk.Text[..remaining]);
            if (chunk.Text.Length > remaining)
            {
                status.Text += " · 已达到 8 MiB 界面缓存上限，剩余内容未载入";
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
            status.Text = $"只读 · 已加载 {tableRows.Count:N0} 行 · 分页加载与回收式虚拟化";
            loadMoreButton.Visibility = page.IsFinal ? Visibility.Collapsed : Visibility.Visible;
            loadMoreButton.IsEnabled = !page.IsFinal;
        }
        catch (OperationCanceledException) { status.Text = "已取消加载。"; }
        catch (InvalidDataException exception) { status.Text = $"表格被安全拒绝：{exception.Message}"; }
        finally { cancelButton.IsEnabled = false; }
    }

    private async Task NavigateSlideAsync(int slideNumber)
    {
        if (document is not ISlidePreviewDocument slides || loadCancellation is null) return;
        if (slideNumber < 1 || slideNumber > slides.SlideCount)
        {
            status.Text = $"页码范围为 1–{slides.SlideCount:N0}。";
            slideNumberBox.Text = currentSlideNumber > 0 ? currentSlideNumber.ToString(CultureInfo.CurrentCulture) : string.Empty;
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
            tableRows.Clear();
            tableRows.Add($"幻灯片 {slide.SlideNumber:N0} / {slides.SlideCount:N0}");
            tableRows.Add(string.IsNullOrWhiteSpace(slide.Text) ? "（此页没有可提取文本）" : slide.Text.Replace(Environment.NewLine, "  │  "));
            status.Text = $"只读 · 幻灯片 {slide.SlideNumber:N0} / {slides.SlideCount:N0} · 切页 {timer.ElapsedMilliseconds:N0} ms · 邻页后台预热";
            _ = PrewarmAdjacentSlidesAsync(slides, slide.SlideNumber, loadCancellation.Token);
        }
        catch (OperationCanceledException) { status.Text = "已取消加载。"; }
        catch (InvalidDataException exception) { status.Text = $"PPTX 被安全拒绝：{exception.Message}"; }
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
        searchResultBox.Visibility = Visibility.Collapsed;
        searchResultBox.SelectedIndex = -1;
        cancelButton.IsEnabled = true;
        var timer = Stopwatch.StartNew();
        try
        {
            await using var searchDocument = await providers.OpenAsync(document.Info.FilePath, cancellationToken: loadCancellation.Token);
            var hits = await ViewerSearchService.SearchAsync(searchDocument, searchBox.Text.Trim(), 50, loadCancellation.Token);
            timer.Stop();

            foreach (var hit in hits) searchResults.Add(new(hit));
            searchResultBox.Visibility = hits.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            status.Text = hits.Length == 0
                ? $"搜索完成 · {timer.ElapsedMilliseconds:N0} ms · 未找到匹配项"
                : $"搜索完成 · {timer.ElapsedMilliseconds:N0} ms · {hits.Length:N0} 个结果（最多显示 50 个）";
        }
        catch (OperationCanceledException) { status.Text = "已取消搜索。"; }
        catch (Exception exception) when (exception is IOException or InvalidDataException or XmlException)
        {
            status.Text = $"搜索被安全终止：{exception.Message}";
        }
        finally { cancelButton.IsEnabled = false; }
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
                else status.Text = "该结果位于当前 8 MiB 界面缓存之外。";
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
            status.Text = $"只读 · 已跳转到第 {oneBasedRow:N0} 行所在页 · 行范围 {first:N0}–{last:N0}";
            loadMoreButton.Visibility = page.IsFinal ? Visibility.Collapsed : Visibility.Visible;
            loadMoreButton.IsEnabled = !page.IsFinal;
            return;
        }

        status.Text = $"无法定位第 {oneBasedRow:N0} 行。";
    }

    private async Task PrewarmAdjacentSlidesAsync(ISlidePreviewDocument slides, int slideNumber, CancellationToken cancellationToken)
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

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
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
            ViewerSearchLocationKind.Slide => $"幻灯片 {Hit.PrimaryIndex:N0} · {Hit.Snippet}",
            ViewerSearchLocationKind.Row => $"第 {Hit.PrimaryIndex:N0} 行 / 第 {Hit.SecondaryIndex:N0} 列 · {Hit.Snippet}",
            _ => $"字符 {Hit.PrimaryIndex + 1:N0} · {Hit.Snippet}"
        };
    }
}
