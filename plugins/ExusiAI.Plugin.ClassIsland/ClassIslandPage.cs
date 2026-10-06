using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ExusiAI.Plugin.ClassIsland;

internal sealed class ClassIslandPage : UserControl
{
    private readonly ClassIslandHost host;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };

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
        var followTheme = new CheckBox { Content = "跟随 ExusiAI 深浅色与强调色",
            IsChecked = host.FollowHostTheme, Margin = new Thickness(0, 18, 0, 0) };
        followTheme.Click += (_, _) =>
        {
            try { host.SetFollowHostTheme(followTheme.IsChecked == true); }
            catch (Exception error)
            {
                followTheme.IsChecked = host.FollowHostTheme;
                status.Text = $"主题选项保存失败：{error.GetBaseException().Message}";
            }
        };
        panel.Children.Add(followTheme);
        panel.Children.Add(new TextBlock { Text = "默认使用 ClassIsland 独立主题；关闭跟随即可恢复原版主题设置。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        Add(buttons, "显示原版信息岛", () => host.Start());
        Add(buttons, "隐藏原版信息岛", () => { host.Hide(); return true; });
        Add(buttons, "打开原版设置", host.OpenSettings);
        Add(buttons, "编辑原版课表", () => host.OpenPage("profile"));
        Add(buttons, "编辑信息岛组件", () => host.OpenPage("edit"));
        Add(buttons, "临时换课", () => host.OpenPage("class-swap"));
        var import = new Button { Content = "导入本机 ClassIsland 数据", Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(14, 8, 14, 8), MinHeight = 40 };
        import.Click += async (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "选择原版 ClassIsland 的 Data 文件夹" };
            var defaultPath = System.IO.Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData), "ClassIsland", "Data");
            if (System.IO.Directory.Exists(defaultPath)) dialog.InitialDirectory = defaultPath;
            if (dialog.ShowDialog() != true) return;
            import.IsEnabled = false;
            try
            {
                var backup = await ClassIslandDataImporter.ImportAsync(dialog.FolderName,
                    host.DataDirectory, host.Dispose, () => host.IsStopped);
                status.Text = $"导入完成。请重启 ExusiAI 以加载原版数据。导入前备份：{backup}";
            }
            catch (Exception error) { status.Text = error.GetBaseException().Message; }
            finally { import.IsEnabled = true; }
        };
        buttons.Children.Add(import);
        panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel };
        Loaded += (_, _) => Refresh();
    }

    private void Add(Panel panel, string caption, Func<bool> action)
    {
        var button = new Button { Content = caption, Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(14, 8, 14, 8), MinHeight = 40 };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await Task.Run(action); Refresh(); }
            catch (Exception error) { status.Text = error.GetBaseException().Message; }
            finally { button.IsEnabled = true; }
        };
        panel.Children.Add(button);
    }

    private void Refresh() => status.Text = host.LastStartupError is { } error
        ? $"原版 ClassIsland 启动失败：{error}"
        : host.IsVisible ? "原版信息岛正在显示。" : "原版信息岛已隐藏。";
}
