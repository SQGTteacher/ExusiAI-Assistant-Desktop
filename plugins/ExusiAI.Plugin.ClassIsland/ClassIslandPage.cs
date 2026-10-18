using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ExusiAI.Plugin.ClassIsland;

internal sealed class ClassIslandPage : UserControl
{
    private readonly ClassIslandHost host;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel controls = new();
    private readonly ComboBox backups = new() { MinWidth = 320, MaxWidth = 620,
        DisplayMemberPath = "Name", Margin = new Thickness(0, 8, 0, 10) };
    private readonly Button cancel = new() { Content = "取消当前操作", IsEnabled = false,
        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
    private CancellationTokenSource? operation;
    private string? message;
    private bool subscribed;

    public ClassIslandPage(ClassIslandHost host)
    {
        this.host = host;
        var panel = new StackPanel { Margin = new Thickness(24), MaxWidth = 680,
            HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock { Text = "ClassIsland", FontSize = 26,
            FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "原版信息岛和设置在 ExusiAI 进程内运行。",
            Margin = new Thickness(0, 8, 0, 18) });
        panel.Children.Add(status);
        panel.Children.Add(controls);
        var startup = new CheckBox { Content = "随 ExusiAI 启动显示信息岛（下次启动生效）",
            IsChecked = host.ShowOnHostStartup, Margin = new Thickness(0, 18, 0, 0) };
        startup.Click += async (_, _) => await Run(async _ =>
        {
            try { host.SetShowOnHostStartup(startup.IsChecked == true); }
            finally { startup.IsChecked = host.ShowOnHostStartup; }
            await Task.CompletedTask;
        });
        controls.Children.Add(startup);
        var followTheme = new CheckBox { Content = "跟随 ExusiAI 深浅色与强调色",
            IsChecked = host.FollowHostTheme, Margin = new Thickness(0, 18, 0, 0) };
        followTheme.Click += async (_, _) => await Run(async _ =>
        {
            try { host.SetFollowHostTheme(followTheme.IsChecked == true); }
            finally { followTheme.IsChecked = host.FollowHostTheme; }
            await Task.CompletedTask;
        });
        controls.Children.Add(followTheme);
        controls.Children.Add(new TextBlock { Text = "默认使用 ClassIsland 独立主题；关闭跟随即可恢复原版主题设置。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        Add(buttons, "显示原版信息岛", token => Command(() => host.StartWithCancellation(token), token));
        Add(buttons, "隐藏原版信息岛", token => Command(() => host.HideWithCancellation(token), token));
        Add(buttons, "打开原版设置", token => OpenPage("settings", token));
        Add(buttons, "编辑原版课表", token => OpenPage("profile", token));
        Add(buttons, "编辑信息岛组件", token => OpenPage("edit", token));
        Add(buttons, "临时换课", token => OpenPage("class-swap", token));
        Add(buttons, "导入本机 ClassIsland 数据", async token =>
        {
            var dialog = new OpenFolderDialog { Title = "选择原版 ClassIsland 的 Data 文件夹" };
            var defaultPath = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData), "ClassIsland", "Data");
            if (Directory.Exists(defaultPath)) dialog.InitialDirectory = defaultPath;
            if (dialog.ShowDialog() == true) await Import(dialog.FolderName, token);
        });
        controls.Children.Add(buttons);
        controls.Children.Add(new TextBlock { Text = "导入前备份（仅包含设置、课表与 Config）",
            Margin = new Thickness(0, 12, 0, 0) });
        controls.Children.Add(backups);
        var backupButtons = new WrapPanel();
        Add(backupButtons, "刷新备份列表", _ => { LoadBackups(); return Task.CompletedTask; });
        Add(backupButtons, "恢复所选备份", async token =>
        {
            if (backups.SelectedItem is not DirectoryInfo backup)
                throw new InvalidOperationException("请先选择一个备份。");
            if (MessageBox.Show("恢复会替换当前设置、课表和 Config，并先备份当前数据。完成后需重启 ExusiAI。是否继续？",
                "恢复 ClassIsland 备份", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await Import(backup.FullName, token);
        });
        controls.Children.Add(backupButtons);
        cancel.Click += (_, _) => { operation?.Cancel(); cancel.IsEnabled = false; };
        panel.Children.Add(cancel);
        Content = new ScrollViewer { Content = panel };
        Loaded += (_, _) =>
        {
            if (!subscribed) { host.StateChanged += OnStateChanged; subscribed = true; }
            try { LoadBackups(); }
            catch (Exception error) { message = error.GetBaseException().Message; }
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            host.StateChanged -= OnStateChanged;
            subscribed = false;
            operation?.Cancel();
        };
    }

    private Task OpenPage(string page, CancellationToken token) =>
        Command(() => host.OpenPageWithCancellation(page, token), token);

    private async Task Command(Func<bool> action, CancellationToken token)
    {
        if (!await Task.Run(action, token))
            throw new InvalidOperationException(host.IsBusy ? "ClassIsland 正在执行其他操作。" :
                host.LastStartupError ?? "ClassIsland 当前无法执行此操作。");
    }

    private async Task Import(string source, CancellationToken token)
    {
        var backup = await host.ImportDataAsync(source, token);
        message = $"数据已替换，请重启 ExusiAI 后加载。替换前备份：{backup}";
        LoadBackups();
    }

    private void LoadBackups()
    {
        var selected = (backups.SelectedItem as DirectoryInfo)?.FullName;
        backups.ItemsSource = ClassIslandDataImporter.ListBackups(host.DataDirectory)
            .Select(path => new DirectoryInfo(path)).ToArray();
        backups.SelectedItem = backups.Items.Cast<DirectoryInfo>().FirstOrDefault(x => x.FullName == selected);
    }

    private void Add(Panel panel, string caption, Func<CancellationToken, Task> action)
    {
        var button = new Button { Content = caption, Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(14, 8, 14, 8), MinHeight = 40 };
        button.Click += async (_, _) => await Run(action);
        panel.Children.Add(button);
    }

    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (operation is not null || host.IsBusy) return;
        using var current = new CancellationTokenSource();
        operation = current;
        message = null;
        Refresh();
        try { await action(current.Token); }
        catch (OperationCanceledException)
        {
            message = "操作已取消。" + (host.RequiresRestart ? "请重启 ExusiAI 后再打开 ClassIsland。" : "");
        }
        catch (Exception error) { message = error.GetBaseException().Message; }
        finally { operation = null; Refresh(); }
    }

    private void OnStateChanged(object? sender, EventArgs args)
    {
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke((Action)(() => { if (IsLoaded) Refresh(); }));
    }

    private void Refresh()
    {
        var busy = operation is not null || host.IsBusy;
        controls.IsEnabled = !busy;
        cancel.IsEnabled = operation is { IsCancellationRequested: false };
        status.Text = busy ? "正在处理 ClassIsland 操作，请稍候。取消后会等待安全清理完成。"
            : (host.LastStartupError is { } error
                ? $"ClassIsland 操作失败：{error}" + (host.RequiresRestart ? " 请重启 ExusiAI 后再次打开。" : "")
                : host.RequiresRestart ? "ClassIsland 已停止。请重启 ExusiAI 后再次打开。"
                : host.IsStopped ? "尚未启动。选择下方操作即可打开 ClassIsland。"
                : host.IsVisible ? "原版信息岛正在显示。" : "原版信息岛已隐藏，后台服务仍在运行。");
        if (!busy && message is not null) status.Text += $"\n上次操作：{message}";
    }
}
