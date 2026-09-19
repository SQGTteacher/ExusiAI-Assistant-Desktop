using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ExusiAI.Plugin.FileViewer;

internal sealed class FileViewerSettingsPage : UserControl
{
    private readonly RecentFilesStore recentFilesStore = new();
    private readonly StackPanel recentFilesPanel = new();

    public FileViewerSettingsPage()
    {
        Content = BuildLayout();
        Loaded += async (_, _) => await RefreshRecentFilesAsync();
    }

    private FrameworkElement BuildLayout()
    {
        var root = new StackPanel();

        root.Children.Add(new TextBlock
        {
            Text = "文件查看器",
            FontSize = 27,
            FontWeight = FontWeights.SemiBold
        });
        root.Children.Add(new TextBlock
        {
            Text = "这里用于文件查看器的入口、最近文件与基础说明。只有选择文件后才会创建独立查看窗口。",
            FontSize = 13,
            Margin = new Thickness(0, 7, 0, 20),
            TextWrapping = TextWrapping.Wrap
        });

        var launchCard = CreateCard();
        var launchContent = new StackPanel();
        launchContent.Children.Add(new TextBlock
        {
            Text = "打开文档",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        launchContent.Children.Add(new TextBlock
        {
            Text = "支持 TXT、Markdown、CSV、DOCX、XLSX 与 PPTX 的安全只读查看。DOC、XLS、PPT、RTF 暂未启用。",
            Margin = new Thickness(0, 6, 0, 14),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });

        var openButton = new Button
        {
            Content = "选择文件并打开查看器",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 180
        };
        openButton.Click += async (_, _) => await PickAndOpenAsync();
        launchContent.Children.Add(openButton);
        launchCard.Child = launchContent;
        root.Children.Add(launchCard);

        root.Children.Add(new TextBlock
        {
            Text = "最近文件",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 22, 0, 10)
        });
        root.Children.Add(recentFilesPanel);

        var safetyCard = CreateCard();
        safetyCard.Margin = new Thickness(0, 22, 0, 0);
        safetyCard.Child = new TextBlock
        {
            Text = "安全默认：只读打开；不执行宏、脚本、外部链接、OLE 或嵌入对象；Open XML 使用 ZIP/XML 安全预算；大文件和复杂内容按既定上限拒绝或分页读取。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        };
        root.Children.Add(safetyCard);

        return new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private async Task PickAndOpenAsync()
    {
        var picker = new OpenFileDialog
        {
            Title = "选择要查看的文件",
            Filter = "支持的文件|*.txt;*.md;*.markdown;*.csv;*.docx;*.xlsx;*.pptx|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog() != true) return;

        await OpenViewerWindowAsync(picker.FileName);
        await RefreshRecentFilesAsync();
    }

    private async Task OpenViewerWindowAsync(string filePath)
    {
        if (!File.Exists(filePath)) return;

        var viewer = new FileViewerPage();
        var window = new Window
        {
            Title = $"ExusiAI 文件查看器 · {Path.GetFileName(filePath)}",
            Width = 1280,
            Height = 820,
            MinWidth = 900,
            MinHeight = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            Content = viewer,
            Background = Application.Current?.TryFindResource("AppBackgroundBrush") as Brush
        };
        window.Closed += (_, _) => viewer.Dispose();
        window.Show();
        await viewer.OpenFileAsync(filePath);
    }

    private async Task RefreshRecentFilesAsync()
    {
        recentFilesPanel.Children.Clear();
        var recent = await recentFilesStore.LoadAsync();

        if (recent.Count == 0)
        {
            recentFilesPanel.Children.Add(new TextBlock
            {
                Text = "还没有最近打开的文件。",
                Opacity = 0.68
            });
            return;
        }

        foreach (var item in recent.Take(8))
        {
            var button = new Button
            {
                Content = item.DisplayName,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = item.Path
            };
            button.SetResourceReference(Button.BackgroundProperty, "SurfaceAltBrush");
            button.SetResourceReference(Button.ForegroundProperty, "TextPrimaryBrush");
            button.Click += async (_, _) =>
            {
                if (File.Exists(item.Path))
                    await OpenViewerWindowAsync(item.Path);
                else
                {
                    await recentFilesStore.RemoveAsync(item.Path);
                    await RefreshRecentFilesAsync();
                }
            };
            recentFilesPanel.Children.Add(button);
        }
    }

    private static Border CreateCard()
    {
        var border = new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1)
        };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return border;
    }
}
