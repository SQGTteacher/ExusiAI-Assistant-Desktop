using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ExusiAI.Core;
using ExusiAI.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ExusiAI.Desktop;

public interface ICrashReporter
{
    string Report(Exception exception, string context, bool showDialog = true);
    FrameworkElement CreateErrorPage(Exception exception, string context);
}

public sealed class CrashReporter(IAppPaths paths, ILogger<CrashReporter> logger) : ICrashReporter
{
    private int showingDialog;

    public string Report(Exception exception, string context, bool showDialog = true)
    {
        var report = BuildReport(exception, context);
        var file = Path.Combine(paths.LogsDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            File.WriteAllText(file, report, Encoding.UTF8);
        }
        catch (Exception writeException) { logger.LogError(writeException, "Crash report could not be written."); }
        logger.LogError(exception, "Unhandled failure in {Context}. Crash report: {CrashFile}", context, file);
        if (showDialog && Interlocked.CompareExchange(ref showingDialog, 1, 0) == 0)
        {
            try { ShowDialog(report, file); }
            finally { Interlocked.Exchange(ref showingDialog, 0); }
        }
        return file;
    }

    public FrameworkElement CreateErrorPage(Exception exception, string context)
    {
        var file = Report(exception, context, false);
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 680 };
        panel.Children.Add(new TextBlock { Text = "此页面未能打开", FontSize = 25, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "故障已被隔离，主程序仍可继续使用。", Margin = new(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBox { Text = exception.ToString(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 190, Margin = new(0, 18, 0, 12) });
        var button = new Button { Content = "打开崩溃日志目录", HorizontalAlignment = HorizontalAlignment.Center };
        button.Click += (_, _) => OpenDirectory(Path.GetDirectoryName(file)!);
        panel.Children.Add(button);
        return panel;
    }

    private static string BuildReport(Exception exception, string context) => $"""
        ExusiAI Assistant Desktop crash report
        Time: {DateTimeOffset.Now:O}
        Version: {ApplicationInfo.PreviewLabel}
        Context: {context}
        OS: {Environment.OSVersion}
        Runtime: {Environment.Version}

        {exception}
        """;

    private static void ShowDialog(string report, string file)
    {
        var details = new TextBox { Text = report, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 14, 0, 14) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var copy = new Button { Content = "复制详情", Margin = new(0, 0, 8, 0) };
        copy.Click += (_, _) => TryAction(() => Clipboard.SetText(report), copy);
        var open = new Button { Content = "打开日志目录", Margin = new(0, 0, 8, 0) };
        open.Click += (_, _) => TryAction(() => OpenDirectory(Path.GetDirectoryName(file)!), open);
        var close = new Button { Content = "继续使用" };
        buttons.Children.Add(copy); buttons.Children.Add(open); buttons.Children.Add(close);
        var root = new Grid { Margin = new(22) }; root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = "ExusiAI 捕获到异常", FontSize = 22, FontWeight = FontWeights.SemiBold }); Grid.SetRow(details, 1); root.Children.Add(details); Grid.SetRow(buttons, 2); root.Children.Add(buttons);
        var window = new Window { Title = "ExusiAI 崩溃报告", Content = root, Width = 760, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterScreen, Owner = Application.Current?.MainWindow };
        close.Click += (_, _) => window.Close();
        window.ShowDialog();
    }

    private static void OpenDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private static void TryAction(Action action, Button source)
    {
        try { action(); }
        catch (Exception exception) { source.ToolTip = exception.Message; }
    }
}
