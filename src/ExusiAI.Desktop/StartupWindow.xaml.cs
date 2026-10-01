using System.Windows;

namespace ExusiAI.Desktop;

public partial class StartupWindow : Window
{
    public StartupWindow() => InitializeComponent();

    public void SetStage(string stage) => StageText.Text = stage;

    public void ShowFailure(string stage, string logPath)
    {
        Title = "ExusiAI 启动失败";
        StageText.Text = $"启动失败 · {stage}";
        DetailsText.Text = $"请检查启动日志：{logPath}";
        StartupProgress.IsIndeterminate = false;
        StartupProgress.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown(-1);
}
