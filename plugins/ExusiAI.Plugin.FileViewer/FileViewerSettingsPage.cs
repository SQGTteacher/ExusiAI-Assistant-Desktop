using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace ExusiAI.Plugin.FileViewer;

internal sealed record FileViewerSettings(bool StartMaximized = false, bool RememberRecentFiles = true, bool PreferDarkTheme = true, int DefaultZoomPercent = 100);

internal static class FileViewerSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExusiAI", "FileViewer", "settings.json");

    public static async Task<FileViewerSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            await using var stream = File.OpenRead(SettingsPath);
            return await JsonSerializer.DeserializeAsync<FileViewerSettings>(stream) ?? new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public static async Task SaveAsync(FileViewerSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporary = SettingsPath + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true });
        File.Move(temporary, SettingsPath, true);
    }
}

internal sealed class FileViewerSettingsPage : UserControl
{
    private readonly CheckBox startMaximized = new() { Content = "启动后最大化查看器" };
    private readonly CheckBox rememberRecent = new() { Content = "在查看器中保留最近文件" };
    private readonly CheckBox darkTheme = new() { Content = "优先使用深色文档工作区" };
    private readonly Slider defaultZoom = new() { Minimum = 75, Maximum = 200, TickFrequency = 25, IsSnapToTickEnabled = true, Width = 240 };
    private readonly TextBlock zoomValue = new();
    private readonly TextBlock status = new() { Opacity = 0.72 };

    public FileViewerSettingsPage()
    {
        Content = BuildLayout();
        Loaded += async (_, _) => await LoadAsync();
    }

    private FrameworkElement BuildLayout()
    {
        var root = new StackPanel
        {
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(28, 26, 28, 30)
        };
        root.Children.Add(new TextBlock { Text = "文件查看器设置", FontSize = 27, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "ExusiAI 插件只管理独立查看器的行为。文档打开、最近文件、搜索与演示均在 ExusiAI Viewer 独立进程中完成。",
            Margin = new Thickness(0, 7, 0, 22), TextWrapping = TextWrapping.Wrap, Opacity = 0.72
        });

        var behavior = CreateCard("启动与外观");
        var behaviorBody = (StackPanel)behavior.Child;
        behaviorBody.Children.Add(startMaximized);
        behaviorBody.Children.Add(rememberRecent);
        behaviorBody.Children.Add(darkTheme);
        behaviorBody.Children.Add(new TextBlock { Text = "默认缩放", Margin = new Thickness(0, 16, 0, 5), FontWeight = FontWeights.SemiBold });
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal };
        defaultZoom.ValueChanged += (_, _) => zoomValue.Text = $"{defaultZoom.Value:0}%";
        zoomRow.Children.Add(defaultZoom);
        zoomValue.Margin = new Thickness(12, 0, 0, 0);
        zoomValue.VerticalAlignment = VerticalAlignment.Center;
        zoomRow.Children.Add(zoomValue);
        behaviorBody.Children.Add(zoomRow);
        root.Children.Add(behavior);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 8) };
        var save = new Button { Content = "保存设置", MinWidth = 110, Margin = new Thickness(0, 0, 10, 0) };
        save.Click += async (_, _) => await SaveAsync();
        var launch = new Button { Content = "启动 ExusiAI Viewer", MinWidth = 160 };
        launch.Click += (_, _) => LaunchViewer();
        actions.Children.Add(save);
        actions.Children.Add(launch);
        root.Children.Add(actions);
        root.Children.Add(status);

        var boundary = CreateCard("进程与安全边界");
        boundary.Margin = new Thickness(0, 18, 0, 0);
        ((StackPanel)boundary.Child).Children.Add(new TextBlock
        {
            Text = "查看器作为独立附属程序运行。崩溃或大文档内存压力不会直接关闭 ExusiAI 主界面；文档继续以只读方式打开，不执行宏、脚本、OLE、嵌入对象或外部链接。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.78
        });
        root.Children.Add(boundary);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private async Task LoadAsync()
    {
        var settings = await FileViewerSettingsStore.LoadAsync();
        startMaximized.IsChecked = settings.StartMaximized;
        rememberRecent.IsChecked = settings.RememberRecentFiles;
        darkTheme.IsChecked = settings.PreferDarkTheme;
        defaultZoom.Value = settings.DefaultZoomPercent;
    }

    private async Task SaveAsync()
    {
        await FileViewerSettingsStore.SaveAsync(new(startMaximized.IsChecked == true, rememberRecent.IsChecked == true, darkTheme.IsChecked == true, (int)defaultZoom.Value));
        status.Text = "设置已保存，将在下次启动查看器时生效。";
    }

    private void LaunchViewer()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "viewer", "ExusiAI.FileViewer.Desktop.exe");
        if (!File.Exists(executable))
        {
            status.Text = "未找到独立查看器程序，请重新构建或安装完整版本。";
            return;
        }
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        status.Text = "ExusiAI Viewer 已启动。";
    }

    private static Border CreateCard(string title)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        var card = new Border { Padding = new Thickness(18), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = body };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return card;
    }
}
