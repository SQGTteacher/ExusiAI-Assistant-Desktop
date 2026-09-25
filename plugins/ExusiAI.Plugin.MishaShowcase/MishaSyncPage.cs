using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class MishaSyncPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly TextBlock status = MishaUi.Note("选择或拖入 ClassIsland 自动备份 ZIP 后会立即同步到 ExusiAI 的独立工作区副本。");
    private readonly StackPanel summary = new();

    public MishaSyncPage(MishaPlatformStore store)
    {
        this.store = store;

        var root = MishaUi.Page(
            "同步",
            "支持 ClassIsland 2.x 自动备份 ZIP。同步会保留 Settings.json、Profiles 与 Config 下的全部备份内容（包括 .bak、主题包和索引），但 ExusiAI 不会执行备份中的主题脚本或其他外部内容。原 ClassIsland 文件不会被修改。");

        var dropZone = new Border
        {
            MinHeight = 150,
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray,
            AllowDrop = true
        };
        dropZone.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        dropZone.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var dropContent = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        dropContent.Children.Add(new TextBlock
        {
            Text = "拖入 ClassIsland 自动备份 ZIP",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var hint = MishaUi.Note("例如 Auto_Backup_*.zip；也可以使用下方按钮手动选择。");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Margin = new Thickness(0, 8, 0, 0);
        dropContent.Children.Add(hint);
        dropZone.Child = dropContent;

        dropZone.PreviewDragOver += DropZone_OnPreviewDragOver;
        dropZone.Drop += DropZone_OnDrop;
        root.Children.Add(dropZone);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var browse = MishaUi.Button("选择备份 ZIP");
        browse.Click += Browse_OnClick;
        actions.Children.Add(browse);
        root.Children.Add(actions);

        status.Margin = new Thickness(0, 12, 0, 12);
        root.Children.Add(status);
        root.Children.Add(new Separator());
        root.Children.Add(summary);

        store.Changed += (_, _) => Dispatcher.Invoke(RefreshCurrentWorkspace);
        RefreshCurrentWorkspace();
        Content = MishaUi.Scroll(root);
    }

    private async void Browse_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 ClassIsland 自动备份",
            Filter = "ClassIsland 自动备份 (*.zip)|*.zip|ZIP 文件 (*.zip)|*.zip"
        };
        if (dialog.ShowDialog() == true)
            await SyncAsync(dialog.FileName);
    }

    private void DropZone_OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetZip(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void DropZone_OnDrop(object sender, DragEventArgs e)
    {
        if (!TryGetZip(e.Data, out var path))
            return;

        e.Handled = true;
        await SyncAsync(path);
    }

    private async Task SyncAsync(string path)
    {
        try
        {
            status.Text = "正在解析并同步 ClassIsland 备份…";
            var result = await store.SyncBackupAsync(path);
            RenderSummary(result);
            status.Text = "同步完成。已切换到新的 ExusiAI ClassIsland 工作区副本，顶部信息岛和设置页会使用该备份中的原生配置。";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            status.Text = $"同步失败：{exception.Message}";
        }
    }

    private void RenderSummary(ClassIslandBackupSummary result)
    {
        summary.Children.Clear();
        summary.Children.Add(MishaUi.Section("本次同步"));
        summary.Children.Add(MishaUi.SettingRow("备份文件", "", ReadOnly(result.ArchivePath)));
        summary.Children.Add(MishaUi.SettingRow("保留文件", "Settings.json + Profiles + Config 原样复制到独立工作区。", ReadOnly(result.TotalFileCount.ToString("N0"), 120)));
        summary.Children.Add(MishaUi.SettingRow("Profile 文件", "", ReadOnly(result.ProfileFileCount.ToString("N0"), 120)));
        summary.Children.Add(MishaUi.SettingRow("Config 文件", "包括组件布局、自动化、管理配置、主题包/主题文件、插件索引及 .bak。", ReadOnly(result.ConfigFileCount.ToString("N0"), 120)));
        summary.Children.Add(MishaUi.SettingRow("解压后大小", "", ReadOnly(FormatBytes(result.TotalUncompressedBytes), 160)));
    }

    private void RefreshCurrentWorkspace()
    {
        if (store.Workspace is null)
            return;

        if (summary.Children.Count == 0)
        {
            summary.Children.Add(MishaUi.Section("当前同步工作区"));
            summary.Children.Add(MishaUi.SettingRow("Settings.json", "", ReadOnly(store.Workspace.SettingsPath)));
            summary.Children.Add(MishaUi.SettingRow("当前档案", "", ReadOnly(store.Workspace.SelectedProfile, 260)));
            summary.Children.Add(MishaUi.SettingRow("组件配置", "", ReadOnly(store.Workspace.CurrentComponentConfig, 220)));
        }
    }

    private static bool TryGetZip(IDataObject data, out string path)
    {
        path = string.Empty;
        if (!data.GetDataPresent(DataFormats.FileDrop) ||
            data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files ||
            !File.Exists(files[0]) ||
            !string.Equals(Path.GetExtension(files[0]), ".zip", StringComparison.OrdinalIgnoreCase))
            return false;

        path = files[0];
        return true;
    }

    private static TextBox ReadOnly(string value, double width = 560) =>
        new()
        {
            Text = value,
            IsReadOnly = true,
            Width = width,
            HorizontalAlignment = HorizontalAlignment.Left
        };

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
        return $"{value:0.##} {units[unit]}";
    }
}
