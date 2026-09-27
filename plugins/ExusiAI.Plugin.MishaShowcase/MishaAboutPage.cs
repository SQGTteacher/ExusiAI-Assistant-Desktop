using System.Windows;
using System.Windows.Controls;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class MishaAboutPage : UserControl
{
    public MishaAboutPage()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 24), MaxWidth = 900 };
        panel.Children.Add(new TextBlock { Text = "关于 ClassIsland 2.2 Misha 移植", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Note("原项目：ClassIsland/ClassIsland；主要作者与维护者 HelloWRC 及 ClassIsland 开发团队、社区贡献者。"));
        panel.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 14) });
        panel.Children.Add(Row("移植者", "SQGTteacher"));
        panel.Children.Add(Row("上游基线", ClassIslandEmbeddedModuleCatalog.UpstreamBranch + " @ " + ClassIslandEmbeddedModuleCatalog.UpstreamCommit[..12]));
        panel.Children.Add(Row("运行结构", "ClassIsland 模块由 ExusiAI 插件进程内宿主管理；不启动 ClassIsland.exe，也不把独立 ClassIsland Desktop 作为子程序包装。"));
        panel.Children.Add(Row("模块映射", "已跟踪 " + ClassIslandEmbeddedModuleCatalog.All.Count + " 项 Misha 源码注册；现有原生、兼容适配和 ExusiAI 宿主映射统一受插件生命周期管理。"));
        panel.Children.Add(Row("许可", "按仓库 THIRD_PARTY_NOTICES 与 GPL/LGPL 边界保留原项目署名。"));
        panel.Children.Add(Row("配置策略", "导入后保存于 ExusiAI 自有工作区；Settings.json、Profiles、ComponentLayouts 与 Automations 始终保持 ClassIsland 原生格式，不另造兼容性模板。"));
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static TextBlock Note(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return block;
    }

    private static FrameworkElement Row(string key, string value)
    {
        var grid = new Grid { MinHeight = 54 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = key, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var text = Note(value);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }
}
