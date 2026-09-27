using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace ExusiAI.Plugin.ClassIsland;

internal sealed class ClassIslandSyncPage : UserControl
{
    private readonly ClassIslandCoreService core;
    private readonly TextBlock coreStatus = new();
    private readonly TextBlock syncStatus = new();
    private readonly TextBlock syncSummary = new();

    public ClassIslandSyncPage(ClassIslandCoreService core)
    {
        this.core = core;
        core.Profiles.ProfileChanged += ProfileChanged;
        Unloaded += (_, _) => core.Profiles.ProfileChanged -= ProfileChanged;

        var root = new StackPanel { Margin = new Thickness(28) };
        root.Children.Add(new TextBlock { Text = "ClassIsland", FontSize = 28, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "ClassIsland 核心现作为可独立分发的 ExusiAI 插件运行。档案、课表、组件和通知统一受 ExusiAI 插件生命周期管理，不需要启动外部 ClassIsland 程序。",
            Margin = new Thickness(0, 8, 0, 18), TextWrapping = TextWrapping.Wrap, Opacity = 0.78
        });
        coreStatus.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(coreStatus);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(ActionButton("打开数据目录", () =>
        {
            Directory.CreateDirectory(core.DataDirectory);
            Process.Start(new ProcessStartInfo(core.DataDirectory) { UseShellExecute = true });
            return Task.CompletedTask;
        }));
        actions.Children.Add(ActionButton("导入档案 JSON", BrowseProfileAsync));
        actions.Children.Add(ActionButton("导出当前档案", ExportProfileAsync));
        root.Children.Add(actions);

        root.Children.Add(new Separator { Margin = new Thickness(0, 22, 0, 22) });
        root.Children.Add(new TextBlock { Text = "同步 ClassIsland 备份", FontSize = 20, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "导入 ClassIsland 2.x 自动备份 ZIP，并原样保留 Settings.json、Profiles、Config 以及模型中的未知字段。导入后由插件内 Profile、Timetable、Component 和 Notification 服务直接使用。",
            Margin = new Thickness(0, 8, 0, 14), TextWrapping = TextWrapping.Wrap, Opacity = 0.78
        });

        var dropZone = new Border
        {
            MinHeight = 140, Padding = new Thickness(24), CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1), AllowDrop = true,
            Child = new TextBlock
            {
                Text = "拖入 ClassIsland 自动备份 ZIP，或点击下方按钮选择", FontSize = 17,
                FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
            }
        };
        dropZone.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        dropZone.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        dropZone.PreviewDragOver += (sender, e) => { e.Effects = TryGetZip(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        dropZone.Drop += async (_, e) => { if (TryGetZip(e.Data, out var path)) await SyncAsync(path); e.Handled = true; };
        root.Children.Add(dropZone);

        var select = ActionButton("选择备份 ZIP", BrowseAndSyncAsync);
        select.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(select);
        syncStatus.Margin = new Thickness(0, 12, 0, 0); syncStatus.TextWrapping = TextWrapping.Wrap;
        syncSummary.Margin = new Thickness(0, 8, 0, 0); syncSummary.TextWrapping = TextWrapping.Wrap; syncSummary.Opacity = 0.78;
        root.Children.Add(syncStatus); root.Children.Add(syncSummary);
        RefreshStatus();
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = root };
    }

    private Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 10, 0), MinWidth = 90 };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); RefreshStatus(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            { syncStatus.Text = $"操作失败：{ex.Message}"; }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private async Task BrowseProfileAsync()
    {
        var dialog = new OpenFileDialog { Title = "导入 ClassIsland Profile", Filter = "ClassIsland Profile (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        await using var stream = File.OpenRead(dialog.FileName);
        var profile = await core.Profiles.ImportAsync(stream, Path.GetFileName(dialog.FileName));
        syncStatus.Text = $"已导入档案：{profile.Name}";
    }

    private async Task ExportProfileAsync()
    {
        var profile = core.Profiles.Current ?? throw new InvalidOperationException("当前没有可导出的档案。");
        var dialog = new SaveFileDialog { Title = "导出 ClassIsland Profile", Filter = "ClassIsland Profile (*.json)|*.json", FileName = $"{profile.Id:N}.json" };
        if (dialog.ShowDialog() != true) return;
        await using var stream = File.Create(dialog.FileName);
        await core.Profiles.ExportAsync(profile, stream);
        syncStatus.Text = $"已导出档案：{profile.Name}";
    }

    private async Task BrowseAndSyncAsync()
    {
        var dialog = new OpenFileDialog { Title = "选择 ClassIsland 自动备份", Filter = "ClassIsland 自动备份 (*.zip)|*.zip|ZIP 文件 (*.zip)|*.zip" };
        if (dialog.ShowDialog() == true) await SyncAsync(dialog.FileName);
    }

    private async Task SyncAsync(string path)
    {
        syncStatus.Text = "正在导入并加载 ClassIsland 备份…"; syncSummary.Text = "";
        var result = await core.ImportBackupAsync(path);
        syncStatus.Text = "同步完成，插件内核心服务已重新加载配置。";
        syncSummary.Text = $"导入 {result.TotalFileCount:N0} 个文件（Profiles {result.ProfileFileCount:N0}，Config {result.ConfigFileCount:N0}），共 {FormatBytes(result.TotalUncompressedBytes)}。";
        RefreshStatus();
    }

    private void ProfileChanged(object? sender, EventArgs e) => Dispatcher.Invoke(RefreshStatus);
    private void RefreshStatus() => coreStatus.Text = core.Profiles.Current is { } profile
        ? $"核心状态：{(core.IsRunning ? "运行中" : "已停止")} · 当前档案：{profile.Name} · 科目 {profile.Subjects.Count} · 时间表 {profile.TimeLayouts.Count} · 课表 {profile.ClassPlans.Count} · 课表群 {profile.ClassPlanGroups.Count} · 组件方案 {core.Components.ComponentConfigs.Count}"
        : $"核心状态：{(core.IsRunning ? "运行中" : "已停止")} · 尚未载入档案";

    private static bool TryGetZip(IDataObject data, out string path)
    {
        path = "";
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files ||
            !File.Exists(files[0]) || !string.Equals(Path.GetExtension(files[0]), ".zip", StringComparison.OrdinalIgnoreCase)) return false;
        path = files[0]; return true;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"]; var value = (double)bytes; var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}
