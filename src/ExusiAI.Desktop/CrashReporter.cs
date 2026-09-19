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
        var crash = Capture(exception, context);
        if (showDialog && Interlocked.CompareExchange(ref showingDialog, 1, 0) == 0)
        {
            try { ShowDialog(crash); }
            finally { Interlocked.Exchange(ref showingDialog, 0); }
        }
        return crash.FilePath;
    }

    public FrameworkElement CreateErrorPage(Exception exception, string context)
    {
        var crash = Capture(exception, context);
        var root = new Grid
        {
            Margin = new Thickness(24),
            MaxWidth = 980,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "此页面未能打开",
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var description = new TextBlock
        {
            Text = "故障已被隔离，主程序仍可继续使用。下面同时提供关键日志和完整日志。",
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(description, 1);
        root.Children.Add(description);

        var reportTabs = CreateReportTabs(crash);
        reportTabs.Margin = new Thickness(0, 18, 0, 14);
        Grid.SetRow(reportTabs, 2);
        root.Children.Add(reportTabs);

        var buttons = CreateActionButtons(crash, HorizontalAlignment.Center);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        return root;
    }

    private CrashArtifact Capture(Exception exception, string context)
    {
        var file = Path.Combine(paths.LogsDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
        var report = BuildReport(exception, context, file);
        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            File.WriteAllText(file, report, Encoding.UTF8);
        }
        catch (Exception writeException)
        {
            logger.LogError(writeException, "Crash report could not be written.");
        }

        logger.LogError(exception, "Unhandled failure in {Context}. Crash report: {CrashFile}", context, file);
        return new(file, BuildCriticalSummary(exception, context, file), report);
    }

    private static string BuildReport(Exception exception, string context, string file) => $"""
        ExusiAI Assistant Desktop crash report
        Time: {DateTimeOffset.Now:O}
        Version: {ApplicationInfo.PreviewLabel}
        Context: {context}
        OS: {Environment.OSVersion}
        Runtime: {Environment.Version}
        Crash log: {file}

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

    private static TabControl CreateReportTabs(CrashArtifact crash)
    {
        var tabs = new TabControl { MinHeight = 280 };

        tabs.Items.Add(new TabItem
        {
            Header = "关键日志",
            Content = new TextBox
            {
                Text = crash.Summary,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(8)
            }
        });

        tabs.Items.Add(new TabItem
        {
            Header = "完整日志",
            Content = new TextBox
            {
                Text = crash.FullReport,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(8)
            }
        });

        return tabs;
    }

    private static WrapPanel CreateActionButtons(CrashArtifact crash, HorizontalAlignment alignment, Action? close = null)
    {
        var buttons = new WrapPanel { HorizontalAlignment = alignment };

        var copy = new Button { Content = "复制关键日志", Margin = new Thickness(0, 0, 8, 8) };
        copy.Click += (_, _) => TryAction(() => Clipboard.SetText(crash.Summary), copy);

        var copyFull = new Button { Content = "复制完整日志", Margin = new Thickness(0, 0, 8, 8) };
        copyFull.Click += (_, _) => TryAction(() => Clipboard.SetText(crash.FullReport), copyFull);

        var openFile = new Button { Content = "打开此次日志", Margin = new Thickness(0, 0, 8, 8) };
        openFile.Click += (_, _) => TryAction(() => OpenFile(crash.FilePath), openFile);

        var issue = new Button { Content = "提交 GitHub Issue", Margin = new Thickness(0, 0, 8, 8) };
        issue.Click += (_, _) => TryAction(OpenIssue, issue);

        var openDirectory = new Button { Content = "打开日志目录", Margin = new Thickness(0, 0, 8, 8) };
        openDirectory.Click += (_, _) => TryAction(() => OpenDirectory(Path.GetDirectoryName(crash.FilePath)!), openDirectory);

        buttons.Children.Add(copy);
        buttons.Children.Add(copyFull);
        buttons.Children.Add(openFile);
        buttons.Children.Add(issue);
        buttons.Children.Add(openDirectory);

        if (close is not null)
        {
            var closeButton = new Button { Content = "继续使用", Margin = new Thickness(0, 0, 0, 8) };
            closeButton.Click += (_, _) => close();
            buttons.Children.Add(closeButton);
        }

        return buttons;
    }

    private static void ShowDialog(CrashArtifact crash)
    {
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "ExusiAI 捕获到异常",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });

        var reportTabs = CreateReportTabs(crash);
        reportTabs.Margin = new Thickness(0, 14, 0, 14);
        Grid.SetRow(reportTabs, 1);
        root.Children.Add(reportTabs);

        var window = new Window
        {
            Title = "ExusiAI 崩溃报告",
            Width = 900,
            Height = 650,
            MinWidth = 760,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Owner = Application.Current?.MainWindow,
            ShowInTaskbar = false
        };

        var buttons = CreateActionButtons(crash, HorizontalAlignment.Right, window.Close);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        window.Content = root;
        window.ShowDialog();
    }

    private static void OpenFile(string file)
    {
        if (!File.Exists(file))
            throw new FileNotFoundException("此次崩溃日志文件不存在或写入失败。", file);
        Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
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

    private sealed record CrashArtifact(string FilePath, string Summary, string FullReport);
}
