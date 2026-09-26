using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace ExusiAI.Plugin.RollCall;

internal sealed class RollCallPage : UserControl
{
    private RollCallSession session = new(Array.Empty<RosterEntry>());
    private readonly TextBlock currentName = new() { Text = "导入名单后开始", FontSize = 48, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock currentDetail = new() { Opacity = 0.68, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock progress = new() { FontSize = 16, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock status = new() { Opacity = 0.72, TextWrapping = TextWrapping.Wrap };
    private readonly ListView roster = new() { MinHeight = 260 };
    private readonly Button drawButton = new() { Content = "抽取下一位", MinWidth = 145, Padding = new Thickness(14, 9, 14, 9) };
    private readonly Button absentButton = new() { Content = "标记缺席", MinWidth = 105, Margin = new Thickness(10, 0, 0, 0) };
    private readonly Button undoButton = new() { Content = "撤销", MinWidth = 80, Margin = new Thickness(10, 0, 0, 0) };

    public RollCallPage()
    {
        Content = BuildLayout();
        drawButton.Click += async (_, _) => { var selected = session.DrawNext(); status.Text = selected is null ? "本轮已全部点名，可以重置后开始新一轮。" : $"已抽取 {selected.Name}"; await SaveAndRefreshAsync(); };
        absentButton.Click += async (_, _) => { session.MarkCurrentAbsent(); await SaveAndRefreshAsync(); };
        undoButton.Click += async (_, _) => { session.Undo(); await SaveAndRefreshAsync(); };
        Loaded += async (_, _) => await LoadAsync();
    }

    private FrameworkElement BuildLayout()
    {
        var root = new Grid { MaxWidth = 1080 };
        root.ColumnDefinitions.Add(new() { Width = new GridLength(3, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) });

        var main = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
        main.Children.Add(new TextBlock { Text = "课堂点名", FontSize = 27, FontWeight = FontWeights.SemiBold });
        main.Children.Add(new TextBlock { Text = "名单只保存在本机；每轮随机且不重复。", Margin = new Thickness(0, 6, 0, 18), Opacity = 0.7 });
        var stage = CreateCard();
        stage.MinHeight = 265;
        var stageBody = (StackPanel)stage.Child;
        stageBody.VerticalAlignment = VerticalAlignment.Center;
        stageBody.Children.Add(currentName);
        stageBody.Children.Add(currentDetail);
        main.Children.Add(stage);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 16) };
        actions.Children.Add(drawButton); actions.Children.Add(absentButton); actions.Children.Add(undoButton);
        main.Children.Add(actions);
        main.Children.Add(status);
        Grid.SetColumn(main, 0); root.Children.Add(main);

        var side = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(progress);
        var import = new Button { Content = "导入名单", Padding = new Thickness(10, 5, 10, 5), HorizontalAlignment = HorizontalAlignment.Right };
        import.Click += async (_, _) => await ImportAsync();
        DockPanel.SetDock(import, Dock.Right); header.Children.Add(import);
        side.Children.Add(header);
        ConfigureRoster(); side.Children.Add(roster);
        var secondary = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var reset = new Button { Content = "重置本轮" }; reset.Click += async (_, _) => { session.ResetRound(); await SaveAndRefreshAsync(); };
        var export = new Button { Content = "导出记录", Margin = new Thickness(9, 0, 0, 0) }; export.Click += (_, _) => Export();
        secondary.Children.Add(reset); secondary.Children.Add(export); side.Children.Add(secondary);
        Grid.SetColumn(side, 1); root.Children.Add(side);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void ConfigureRoster()
    {
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "姓名", DisplayMemberBinding = new Binding(nameof(RollCallStudent.Name)), Width = 130 });
        view.Columns.Add(new GridViewColumn { Header = "学号", DisplayMemberBinding = new Binding(nameof(RollCallStudent.StudentId)), Width = 90 });
        view.Columns.Add(new GridViewColumn { Header = "状态", DisplayMemberBinding = new Binding(nameof(RollCallStudent.Status)), Width = 70 });
        roster.View = view;
    }

    private async Task LoadAsync()
    {
        var snapshot = await RollCallStore.LoadAsync();
        if (snapshot is not null) session = new(snapshot);
        Refresh();
    }

    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Title = "导入班级名单", Filter = "名单文件 (*.xlsx;*.csv;*.tsv;*.txt)|*.xlsx;*.csv;*.tsv;*.txt|所有文件 (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var entries = await RosterImporter.ParseFileAsync(dialog.FileName);
            if (entries.Count == 0) { status.Text = "没有识别到姓名，请检查文件内容或表头。"; return; }
            session = new(entries); status.Text = $"已导入 {entries.Count} 名学生。"; await SaveAndRefreshAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { status.Text = $"导入失败：{exception.Message}"; }
    }

    private void Export()
    {
        if (session.Events.Count == 0) { status.Text = "当前还没有点名记录。"; return; }
        var dialog = new SaveFileDialog { Title = "导出课堂点名记录", Filter = "CSV 文件 (*.csv)|*.csv", FileName = $"点名记录-{DateTime.Now:yyyyMMdd-HHmm}.csv" };
        if (dialog.ShowDialog() != true) return;
        var lines = new List<string> { "时间,姓名,学号,状态" };
        lines.AddRange(session.Events.Select(item => $"{Csv(item.Time.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"))},{Csv(item.Name)},{Csv(item.StudentId)},{Csv(StatusText(item.Status))}"));
        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true)); status.Text = "课堂记录已导出。";
    }

    private async Task SaveAndRefreshAsync() { await RollCallStore.SaveAsync(session.Snapshot()); Refresh(); }

    private void Refresh()
    {
        roster.ItemsSource = null; roster.ItemsSource = session.Students;
        progress.Text = $"名单 {session.Students.Count} 人 · 剩余 {session.RemainingCount} 人";
        currentName.Text = session.Current?.Name ?? (session.Students.Count == 0 ? "导入名单后开始" : "准备开始");
        currentDetail.Text = session.Current is null ? "" : string.Join(" · ", new[] { session.Current.StudentId, session.Current.ClassName, StatusText(session.Current.Status) }.Where(value => !string.IsNullOrWhiteSpace(value)));
        drawButton.IsEnabled = session.RemainingCount > 0;
        absentButton.IsEnabled = session.Current is not null && session.Current.Status != RollCallStatus.Absent;
        undoButton.IsEnabled = session.Current is not null;
    }

    private static string StatusText(RollCallStatus value) => value switch { RollCallStatus.Called => "已点", RollCallStatus.Absent => "缺席", _ => "待点" };
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static Border CreateCard()
    {
        var card = new Border { Padding = new Thickness(24), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Child = new StackPanel() };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); return card;
    }
}
