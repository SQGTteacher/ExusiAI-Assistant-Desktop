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
        panel.Children.Add(Row("上游基线", "develop/v2/misha-alpha"));
        panel.Children.Add(Row("许可", "按仓库 THIRD_PARTY_NOTICES 与 GPL/LGPL 边界保留原项目署名。"));
        panel.Children.Add(Row("配置策略", "使用 ClassIsland 原生 Settings.json、Profiles、ComponentLayouts 与 Automations 格式；ExusiAI 不另造兼容性模板。"));
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
