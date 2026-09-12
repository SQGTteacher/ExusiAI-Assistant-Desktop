using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Plugin.Sample;

public sealed class SamplePlugin : ExtensionPluginBase, IWpfNavigationExtension
{
    public override async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);
        context.Logger.Information("Sample plugin initialized.");
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Context.Logger.Information("Sample plugin started.");
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        Context.Logger.Information("Sample plugin stopped.");
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("sample.hello", "示例插件", "✦", static () => new SamplePage())
    ];
}

internal sealed class SamplePage : UserControl
{
    public SamplePage()
    {
        var badgeText = new TextBlock { Text = "SAMPLE EXTENSION", FontWeight = FontWeights.SemiBold };
        badgeText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var badge = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = badgeText
        };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        var panel = new StackPanel { MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(badge);
        panel.Children.Add(new TextBlock { Text = "Hello from ExusiAI Plugin!", FontSize = 34, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 10) });
        panel.Children.Add(new TextBlock { Text = "这个页面来自独立加载的示例包，宿主并不知道它的具体 UI。", FontSize = 16, Opacity = 0.68, TextWrapping = TextWrapping.Wrap });
        Content = panel;
    }
}
