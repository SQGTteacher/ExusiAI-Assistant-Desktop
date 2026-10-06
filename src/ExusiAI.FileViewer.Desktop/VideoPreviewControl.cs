using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;

namespace ExusiAI.FileViewer.Desktop;

/// <summary>Owns decoder lifetime; VLC callbacks never directly modify WPF controls.</summary>
internal sealed class VideoPreviewControl : Grid, IDisposable
{
    public event EventHandler? BackRequested;
    private readonly Button back = new() { Content = "返回幻灯片", Visibility = Visibility.Collapsed, Margin = new Thickness(4) };
    private readonly VideoView view = new();
    private readonly Button play = new() { Content = "播放", MinWidth = 68, Margin = new Thickness(4) };
    private readonly Slider seek = new() { Minimum = 0, Maximum = 1, MinWidth = 120, Margin = new Thickness(12, 0, 12, 0), IsEnabled = false };
    private readonly Slider volume = new() { Minimum = 0, Maximum = 100, Value = 80, Width = 90, Margin = new Thickness(8, 0, 8, 0) };
    private readonly TextBlock message = new() { Text = "点击播放开始本地解码", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private LibVLC? vlc;
    private MediaPlayer? player;
    private bool dragging;
    private bool updating;
    private int generation;

    public VideoPreviewControl()
    {
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(view);
        var controls = new DockPanel { Margin = new Thickness(12, 8, 12, 8) };
        controls.Children.Add(back);
        back.Click += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        controls.Children.Add(play);
        var stop = new Button { Content = "停止", MinWidth = 60, Margin = new Thickness(4) };
        controls.Children.Add(stop);
        DockPanel.SetDock(volume, Dock.Right);
        controls.Children.Add(volume);
        controls.Children.Add(seek);
        SetRow(controls, 1); Children.Add(controls);
        SetRow(message, 2); Children.Add(message);
        play.Click += (_, _) =>
        {
            if (player is null) return;
            if (player.IsPlaying) player.Pause();
            else if (!player.Play()) message.Text = "无法播放：视频损坏或解码器不支持此文件。";
            else timer.Start();
        };
        stop.Click += (_, _) => { player?.Stop(); seek.Value = 0; play.Content = "播放"; };
        volume.ValueChanged += (_, _) => { if (player is not null) player.Volume = (int)volume.Value; };
        seek.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => dragging = true));
        seek.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            dragging = false;
            if (player?.IsSeekable == true) player.Time = (long)seek.Value;
        }));
        seek.ValueChanged += (_, _) =>
        {
            if (!dragging && !updating && player?.IsSeekable == true) player.Time = (long)seek.Value;
        };
        timer.Tick += (_, _) =>
        {
            if (player is null) return;
            updating = true;
            seek.Maximum = Math.Max(1, player.Length);
            seek.IsEnabled = player.IsSeekable;
            if (!dragging) seek.Value = Math.Clamp(player.Time, 0, seek.Maximum);
            play.Content = player.IsPlaying ? "暂停" : "播放";
            if (player.Length > 0)
                message.Text = $"{TimeSpan.FromMilliseconds(Math.Max(0, player.Time)):hh\\:mm\\:ss} / {TimeSpan.FromMilliseconds(player.Length):hh\\:mm\\:ss} · LibVLC 本地解码";
            updating = false;
        };
    }

    public void Open(string filePath, bool embedded = false)
    {
        Close();
        back.Visibility = embedded ? Visibility.Visible : Visibility.Collapsed;
        var currentGeneration = generation;
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            vlc = new LibVLC("--no-video-title-show", "--no-metadata-network-access");
            player = new MediaPlayer(vlc) { Volume = (int)volume.Value };
            player.EncounteredError += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (currentGeneration != generation) return;
                timer.Stop();
                message.Text = "视频解码失败：请检查文件是否损坏、加密或使用不支持的编码。";
                play.Content = "重试";
            }));
            using var media = new Media(vlc, Path.GetFullPath(filePath), FromType.FromPath);
            player.Media = media;
            view.MediaPlayer = player;
            message.Text = "点击播放开始本地解码";
            timer.Start();
        }
        catch (Exception exception) when (exception is VLCException or DllNotFoundException or BadImageFormatException)
        {
            Close();
            message.Text = "无法初始化视频解码库：" + exception.Message;
        }
    }

    public void Close()
    {
        generation++;
        timer.Stop();
        view.MediaPlayer = null;
        player?.Stop();
        player?.Dispose(); player = null;
        vlc?.Dispose(); vlc = null;
        updating = true;
        seek.Value = 0; seek.IsEnabled = false;
        updating = false;
        play.Content = "播放";
    }
    public void Dispose() => Close();
}
