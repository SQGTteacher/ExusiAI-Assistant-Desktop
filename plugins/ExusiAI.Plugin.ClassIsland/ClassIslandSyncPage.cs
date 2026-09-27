using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace ExusiAI.Plugin.ClassIsland;

internal sealed class ClassIslandSyncPage : UserControl
{
    private readonly ClassIslandRuntimeHost runtime;
    private readonly TextBlock runtimeStatus = new();
    private readonly TextBlock syncStatus = new();
    private readonly TextBlock syncSummary = new();

    public ClassIslandSyncPage(ClassIslandRuntimeHost runtime)
    {
        this.runtime = runtime;
        runtime.StatusChanged += RuntimeOnStatusChanged;
        Unloaded += (_, _) => runtime.StatusChanged -= RuntimeOnStatusChanged;

        var root = new StackPanel { Margin = new Thickness(28) };
        root.Children.Add(new TextBlock { Text = "ClassIsland", FontSize = 28, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = $"原生内置运行时 {ClassIslandRuntimeDescriptor.RuntimeVersion}。ExusiAI 不再复刻 ClassIsland 页面、组件或自动化逻辑；完整功能由上游运行核心直接提供。",
            Margin = new Thickness(0, 8, 0, 18),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });
        runtimeStatus.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(runtimeStatus);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(ActionButton("启动", () => runtime.StartAsync()));
        actions.Children.Add(ActionButton("重启", () => runtime.RestartAsync()));
        actions.Children.Add(ActionButton("停止", () => runtime.StopAsync()));
        actions.Children.Add(ActionButton("打开数据目录", () =>
        {
            Directory.CreateDirectory(runtime.DataDirectory);
            Process.Start(new ProcessStartInfo(runtime.DataDirectory) { UseShellExecute = true });
            return Task.CompletedTask;
        }));
        root.Children.Add(actions);

        root.Children.Add(new Separator { Margin = new Thickness(0, 22, 0, 22) });
        root.Children.Add(new TextBlock { Text = "同步 ClassIsland 备份", FontSize = 20, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "保留原同步功能：导入 ClassIsland 2.x 自动备份 ZIP，直接写入原生运行时的 Settings.json、Profiles 与 Config。同步时会停止 ClassIsland；失败自动回滚；日志、缓存和其他 data 内容不会被删除。",
            Margin = new Thickness(0, 8, 0, 14),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });

        var dropZone = new Border
        {
            MinHeight = 140,
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            AllowDrop = true,
            Child = new TextBlock
            {
                Text = "拖入 ClassIsland 自动备份 ZIP，或点击下方按钮选择",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            }
        };
        dropZone.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        dropZone.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        dropZone.PreviewDragOver += (sender, e) =>
        {
            e.Effects = TryGetZip(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        dropZone.Drop += async (_, e) =>
        {
            if (!TryGetZip(e.Data, out var path)) return;
            e.Handled = true;
            await SyncAsync(path);
        };
        root.Children.Add(dropZone);

        var select = ActionButton("选择备份 ZIP", BrowseAndSyncAsync);
        select.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(select);
        syncStatus.Margin = new Thickness(0, 12, 0, 0);
        syncStatus.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(syncStatus);
        syncSummary.Margin = new Thickness(0, 8, 0, 0);
        syncSummary.TextWrapping = TextWrapping.Wrap;
        syncSummary.Opacity = 0.78;
        root.Children.Add(syncSummary);

        RefreshRuntimeStatus();
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root
        };
    }

    private Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 10, 0), MinWidth = 90 };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); RefreshRuntimeStatus(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                syncStatus.Text = $"操作失败：{ex.Message}";
            }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private async Task BrowseAndSyncAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 ClassIsland 自动备份",
            Filter = "ClassIsland 自动备份 (*.zip)|*.zip|ZIP 文件 (*.zip)|*.zip"
        };
        if (dialog.ShowDialog() == true) await SyncAsync(dialog.FileName);
    }

    private async Task SyncAsync(string path)
    {
        syncStatus.Text = "正在停止原生运行时并同步备份…";
        syncSummary.Text = "";
        try
        {
            var result = await runtime.SyncBackupAsync(path);
            syncStatus.Text = "同步完成。ClassIsland 已直接使用该配置；同步前若正在运行，现已恢复运行。";
            syncSummary.Text = $"保留 {result.TotalFileCount:N0} 个文件（Profiles {result.ProfileFileCount:N0}，Config {result.ConfigFileCount:N0}），解压后 {FormatBytes(result.TotalUncompressedBytes)}。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            syncStatus.Text = $"同步失败：{ex.Message}";
        }
        RefreshRuntimeStatus();
    }

    private void RuntimeOnStatusChanged(object? sender, EventArgs e) => Dispatcher.Invoke(RefreshRuntimeStatus);
    private void RefreshRuntimeStatus() => runtimeStatus.Text = runtime.IsRunning
        ? $"状态：运行中 · ClassIsland {ClassIslandRuntimeDescriptor.RuntimeVersion}"
        : $"状态：已停止 · ClassIsland {ClassIslandRuntimeDescriptor.RuntimeVersion}";

    private static bool TryGetZip(IDataObject data, out string path)
    {
        path = "";
        if (!data.GetDataPresent(DataFormats.FileDrop) ||
            data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files ||
            !File.Exists(files[0]) ||
            !string.Equals(Path.GetExtension(files[0]), ".zip", StringComparison.OrdinalIgnoreCase))
            return false;
        path = files[0];
        return true;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}
