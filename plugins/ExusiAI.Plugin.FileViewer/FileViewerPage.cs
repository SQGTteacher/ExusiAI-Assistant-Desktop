using System.Collections.ObjectModel;
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
    private readonly ObservableCollection<string> csvRows = [];
    private readonly ListBox csvPreview;
    private readonly Button loadMoreButton = new() { Content = "加载下一页", IsEnabled = false, Visibility = Visibility.Collapsed };
    private readonly Button previousSlideButton = new() { Content = "上一页", IsEnabled = false, Visibility = Visibility.Collapsed };
    private readonly Button nextSlideButton = new() { Content = "下一页", IsEnabled = false, Visibility = Visibility.Collapsed };
    private readonly TextBox slideNumberBox = new() { Width = 58, Visibility = Visibility.Collapsed, ToolTip = "输入幻灯片页码并按 Enter" };
    private readonly Button cancelButton = new() { Content = "取消", IsEnabled = false };
    private readonly TextBox searchBox = new() { MinWidth = 180, ToolTip = "在已加载的文本中搜索" };
    private CancellationTokenSource? loadCancellation;
    private ViewerDocument? document;
    private IAsyncEnumerator<TabularPage>? csvPages;
    private IAsyncEnumerator<SlidePreview>? slidePages;
    private int currentSlideNumber;
    private bool disposed;

    public FileViewerPage()
    {
        csvPreview = new ListBox
        {
            ItemsSource = csvRows,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Visibility = Visibility.Collapsed,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        VirtualizingPanel.SetIsVirtualizing(csvPreview, true);
        VirtualizingPanel.SetVirtualizationMode(csvPreview, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(csvPreview, true);

        var openButton = new Button { Content = "打开文件", MinWidth = 96 };
        openButton.Click += OpenButton_OnClick;
        cancelButton.Click += (_, _) => loadCancellation?.Cancel();
        loadMoreButton.Click += LoadMoreButton_OnClick;
        previousSlideButton.Click += async (_, _) => await NavigateSlideAsync(currentSlideNumber - 1);
        nextSlideButton.Click += async (_, _) => await NavigateSlideAsync(currentSlideNumber + 1);
        slideNumberBox.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && int.TryParse(slideNumberBox.Text, out var target))
                await NavigateSlideAsync(target);
        };
        var searchButton = new Button { Content = "查找下一个" };
        searchButton.Click += (_, _) => FindNext();
        searchBox.KeyDown += (_, args) => { if (args.Key == Key.Enter) FindNext(); };
        var zoom = new Slider { Minimum = 11, Maximum = 28, Value = 15, Width = 120, TickFrequency = 1, IsSnapToTickEnabled = true };
        zoom.ValueChanged += (_, _) =>
        {
            textPreview.FontSize = zoom.Value;
            csvPreview.FontSize = Math.Max(11, zoom.Value - 1);
        };

        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var element in new FrameworkElement[] { openButton, cancelButton, searchBox, searchButton, loadMoreButton, previousSlideButton, slideNumberBox, nextSlideButton, new TextBlock { Text = "缩放", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) }, zoom })
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
        content.Children.Add(csvPreview);
        var layout = new Grid { Margin = new Thickness(24) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(header);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);
        Content = layout;
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
        csvRows.Clear();
        textPreview.Visibility = Visibility.Collapsed;
        csvPreview.Visibility = Visibility.Collapsed;
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
            document = await providers.OpenAsync(filePath, cancellationToken: loadCancellation.Token);
            title.Text = $"{document.Info.DisplayName} · {document.Info.FormatName}";
            status.Text = $"只读 · {FormatBytes(document.Info.Length)} · 不执行宏、脚本或外部内容";
            switch (document)
            {
                case ITextPreviewDocument text:
                    textPreview.Visibility = Visibility.Visible;
                    await LoadTextPreviewAsync(text, loadCancellation.Token);
                    break;
                case ITabularPreviewDocument csv:
                    csvPreview.Visibility = Visibility.Visible;
                    loadMoreButton.Visibility = Visibility.Visible;
                    csvPages = csv.ReadPagesAsync(loadCancellation.Token).GetAsyncEnumerator(loadCancellation.Token);
                    await LoadNextCsvPageAsync();
                    break;
                case ISlidePreviewDocument slides:
                    csvPreview.Visibility = Visibility.Visible;
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

    private async void LoadMoreButton_OnClick(object sender, RoutedEventArgs e) => await LoadNextCsvPageAsync();

    private async Task LoadNextCsvPageAsync()
    {
        if (csvPages is null) return;
        loadMoreButton.IsEnabled = false;
        cancelButton.IsEnabled = true;
        try
        {
            if (!await csvPages.MoveNextAsync())
            {
                loadMoreButton.Visibility = Visibility.Collapsed;
                return;
            }
            var page = csvPages.Current;
            foreach (var row in page.Rows) csvRows.Add(string.Join("  │  ", row));
            status.Text = $"只读 · 已加载 {csvRows.Count:N0} 行 · 分页加载与回收式虚拟化";
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
            var slide = await slides.ReadSlideAsync(slideNumber, loadCancellation.Token);
            currentSlideNumber = slide.SlideNumber;
            slideNumberBox.Text = slide.SlideNumber.ToString(CultureInfo.CurrentCulture);
            csvRows.Clear();
            csvRows.Add($"幻灯片 {slide.SlideNumber:N0} / {slides.SlideCount:N0}");
            csvRows.Add(string.IsNullOrWhiteSpace(slide.Text) ? "（此页没有可提取文本）" : slide.Text.Replace(Environment.NewLine, "  │  "));
            status.Text = $"只读 · 幻灯片 {slide.SlideNumber:N0} / {slides.SlideCount:N0} · 支持随机跳转与有界缓存";
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

    private void FindNext()
    {
        if (textPreview.Visibility != Visibility.Visible || string.IsNullOrEmpty(searchBox.Text)) return;
        var start = Math.Max(0, textPreview.SelectionStart + textPreview.SelectionLength);
        var index = textPreview.Text.IndexOf(searchBox.Text, start, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0 && start > 0) index = textPreview.Text.IndexOf(searchBox.Text, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0) { status.Text = "在已加载内容中未找到。"; return; }
        textPreview.Select(index, searchBox.Text.Length);
        textPreview.Focus();
        textPreview.ScrollToLine(textPreview.GetLineIndexFromCharacterIndex(index));
    }

    private async Task CloseDocumentAsync()
    {
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = null;
        if (csvPages is not null) await csvPages.DisposeAsync();
        csvPages = null;
        if (slidePages is not null) await slidePages.DisposeAsync();
        slidePages = null;
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
}
