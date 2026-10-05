using System.Windows;
using System.Windows.Controls;

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
        var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        Add(buttons, "显示原版信息岛", () => host.Start());
        Add(buttons, "隐藏原版信息岛", () => { host.Hide(); return true; });
        Add(buttons, "打开原版设置", () => { if (!host.Start()) return false; host.OpenSettings(); return true; });
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
