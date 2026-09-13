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
    private const string NewIssueUrl = "https://github.com/SQGTteacher/ExusiAI-Assistant-Desktop/issues/new?template=bug_report.yml";
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
            try { ShowDialog(exception, context, report, file); }
            finally { Interlocked.Exchange(ref showingDialog, 0); }
        }
        return file;
    }

    public FrameworkElement CreateErrorPage(Exception exception, string context)
    {
        var file = Report(exception, context, false);
        var summary = BuildCriticalSummary(exception, context, file);
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 680 };
        panel.Children.Add(new TextBlock { Text = "此页面未能打开", FontSize = 25, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "故障已被隔离，主程序仍可继续使用。", Margin = new(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBox { Text = summary, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 190, Margin = new(0, 18, 0, 12) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var copy = new Button { Content = "复制关键日志", Margin = new(0, 0, 8, 0) }; copy.Click += (_, _) => TryAction(() => Clipboard.SetText(summary), copy);
        var issue = new Button { Content = "提交 GitHub Issue", Margin = new(0, 0, 8, 0) }; issue.Click += (_, _) => TryAction(OpenIssue, issue);
        var logs = new Button { Content = "打开日志目录" }; logs.Click += (_, _) => TryAction(() => OpenDirectory(Path.GetDirectoryName(file)!), logs);
        buttons.Children.Add(copy); buttons.Children.Add(issue); buttons.Children.Add(logs); panel.Children.Add(buttons);
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

    private static string BuildCriticalSummary(Exception exception, string context, string file)
    {
        var root = exception;
        while (root.InnerException is not null) root = root.InnerException;
        var frames = (root.StackTrace ?? exception.StackTrace ?? "没有可用堆栈")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(10);
        return $"""
            【请随 Issue 一并提交以下关键日志】
            版本：{ApplicationInfo.PreviewLabel}
            位置：{context}
            异常：{root.GetType().FullName}
            消息：{root.Message}
            HRESULT：0x{root.HResult:X8}
            系统：{Environment.OSVersion} / .NET {Environment.Version}
            日志：{file}

            关键堆栈：
            {string.Join(Environment.NewLine, frames)}
            """;
    }

    private static void ShowDialog(Exception exception, string context, string report, string file)
    {
        var summary = BuildCriticalSummary(exception, context, file);
        var content = new Grid(); content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var summaryBox = new TextBox { Text = summary, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 155, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var details = new TextBox { Text = report, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 10, 0, 0) };
        content.Children.Add(summaryBox); Grid.SetRow(details, 1); content.Children.Add(details);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var copy = new Button { Content = "复制关键日志", Margin = new(0, 0, 8, 0) };
        copy.Click += (_, _) => TryAction(() => Clipboard.SetText(summary), copy);
        var copyFull = new Button { Content = "复制完整日志", Margin = new(0, 0, 8, 0) };
        copyFull.Click += (_, _) => TryAction(() => Clipboard.SetText(report), copyFull);
        var issue = new Button { Content = "提交 GitHub Issue", Margin = new(0, 0, 8, 0) };
        issue.Click += (_, _) => TryAction(OpenIssue, issue);
        var open = new Button { Content = "打开日志目录", Margin = new(0, 0, 8, 0) };
        open.Click += (_, _) => TryAction(() => OpenDirectory(Path.GetDirectoryName(file)!), open);
        var close = new Button { Content = "继续使用" };
        buttons.Children.Add(copy); buttons.Children.Add(copyFull); buttons.Children.Add(issue); buttons.Children.Add(open); buttons.Children.Add(close);
        var root = new Grid { Margin = new(22) }; root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = "ExusiAI 捕获到异常", FontSize = 22, FontWeight = FontWeights.SemiBold }); Grid.SetRow(content, 1); content.Margin = new(0, 14, 0, 14); root.Children.Add(content); Grid.SetRow(buttons, 2); root.Children.Add(buttons);
        var window = new Window { Title = "ExusiAI 崩溃报告", Content = root, Width = 900, Height = 650, MinWidth = 760, MinHeight = 520, WindowStartupLocation = WindowStartupLocation.CenterScreen, Owner = Application.Current?.MainWindow };
        close.Click += (_, _) => window.Close();
        window.ShowDialog();
    }

    private static void OpenDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private static void OpenIssue() => Process.Start(new ProcessStartInfo(NewIssueUrl) { UseShellExecute = true });

    private static void TryAction(Action action, Button source)
    {
        try { action(); }
        catch (Exception exception) { source.ToolTip = exception.Message; }
    }
}
