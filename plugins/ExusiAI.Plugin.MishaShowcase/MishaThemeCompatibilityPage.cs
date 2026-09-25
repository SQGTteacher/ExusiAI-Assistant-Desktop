using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class MishaThemeCompatibilityPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly StackPanel themeList = new();
    private readonly StackPanel diagnostics = new();
    private readonly TextBlock status = MishaUi.Note("");

    public MishaThemeCompatibilityPage(MishaPlatformStore store)
    {
        this.store = store;

        var root = MishaUi.Page(
            "ClassIsland 主题",
            "此页使用安全兼容层解析 ClassIsland EnabledThemes.json、manifest.yml 与 AXAML 资源/样式。不会执行主题包中的 Python、脚本、MarkupExtension、任意代码或网络资源；后续 Misha 组件可复用同一份解析结果。");

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var reload = MishaUi.Button("重新扫描主题");
        reload.Click += async (_, _) =>
        {
            try
            {
                await store.ReloadThemesAsync();
                status.Text = "主题已重新解析，信息岛已请求刷新。";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                status.Text = $"主题重新解析失败：{exception.Message}";
            }
        };
        actions.Children.Add(reload);
        root.Children.Add(actions);

        status.Margin = new Thickness(0, 10, 0, 10);
        root.Children.Add(status);
        root.Children.Add(MishaUi.Section("启用顺序与兼容状态"));
        root.Children.Add(themeList);
        root.Children.Add(MishaUi.Section("兼容层说明与诊断"));
        root.Children.Add(MishaUi.Note(
            "当前安全支持：Color、SolidColorBrush、LinearGradientBrush、RadialGradientBrush、DrawingBrush、ConicGradientBrush（WPF 近似）、Dynamic/StaticResource、简单类型/类名/附加属性等值选择器，以及常用 Border/字体/尺寸 Setter。复杂伪类、组合选择器、外部 StyleInclude 和可执行内容只记录诊断，不会执行。"));
        root.Children.Add(diagnostics);

        store.Changed += (_, _) => Dispatcher.Invoke(Refresh);
        Loaded += (_, _) => Refresh();
        Content = MishaUi.Scroll(root);
    }

    private void Refresh()
    {
        themeList.Children.Clear();
        diagnostics.Children.Clear();

        var workspace = store.Workspace;
        if (workspace is null)
        {
            status.Text = "尚未连接 ClassIsland 工作区。";
            return;
        }

        var snapshot = store.ThemeSnapshot;
        status.Text =
            $"EnabledThemes：{snapshot.EnabledThemeIds.Count} 项；安全区：{snapshot.ActualVerticalSafeAreaPx:0.##} px；" +
            $"已发现主题源：{snapshot.Packages.Count} 项。";

        foreach (var package in GetDisplayPackages(snapshot))
            themeList.Children.Add(BuildThemeRow(snapshot, package));

        var messages = snapshot.Packages
            .SelectMany(x => x.Diagnostics.Select(message => $"{x.Manifest.Name} ({x.Manifest.Id})：{message}"))
            .Concat(snapshot.Diagnostics)
            .Distinct(StringComparer.Ordinal)
            .Take(20)
            .ToArray();

        if (messages.Length == 0)
        {
            diagnostics.Children.Add(MishaUi.Note("当前没有兼容层诊断。"));
            return;
        }

        foreach (var message in messages)
        {
            var note = MishaUi.Note("• " + message);
            note.Margin = new Thickness(0, 0, 0, 6);
            diagnostics.Children.Add(note);
        }
    }

    private FrameworkElement BuildThemeRow(
        ClassIslandThemeSnapshot snapshot,
        ClassIslandThemePackage package)
    {
        var panel = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal };

        var enabled = new CheckBox
        {
            Content = package.Manifest.Name,
            IsChecked = package.Enabled,
            FontWeight = FontWeights.SemiBold,
            MinWidth = 260,
            VerticalAlignment = VerticalAlignment.Center
        };
        enabled.Checked += async (_, _) => await ToggleAsync(package.Manifest.Id, true);
        enabled.Unchecked += async (_, _) => await ToggleAsync(package.Manifest.Id, false);
        header.Children.Add(enabled);

        if (package.Enabled)
        {
            var up = MishaUi.Button("上移", true);
            up.Padding = new Thickness(10, 4, 10, 4);
            up.Click += async (_, _) => await MoveAsync(package.Manifest.Id, -1);
            var down = MishaUi.Button("下移", true);
            down.Padding = new Thickness(10, 4, 10, 4);
            down.Click += async (_, _) => await MoveAsync(package.Manifest.Id, 1);
            header.Children.Add(up);
            header.Children.Add(down);
        }

        panel.Children.Add(header);

        var description = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 2),
            Text = BuildDescription(package)
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        panel.Children.Add(description);

        var compatibility = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Text = BuildCompatibility(package)
        };
        compatibility.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        panel.Children.Add(compatibility);

        return MishaUi.SettingRow(
            package.Manifest.Id,
            package.SourcePath == "integrated"
                ? "ClassIsland 内置/插件注册主题；没有本地 AXAML 时作为宿主基线保留。"
                : package.SourcePath,
            panel);
    }

    private static string BuildDescription(ClassIslandThemePackage package)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(package.Manifest.Author))
            parts.Add($"作者 {package.Manifest.Author}");
        if (!string.IsNullOrWhiteSpace(package.Manifest.Version))
            parts.Add($"版本 {package.Manifest.Version}");
        if (!string.IsNullOrWhiteSpace(package.Manifest.Description))
            parts.Add(package.Manifest.Description);
        if (package.Manifest.VerticalSafeAreaPx > 0)
            parts.Add($"垂直安全区 {package.Manifest.VerticalSafeAreaPx:0.##} px");
        return parts.Count == 0 ? "没有额外元数据。" : string.Join(" · ", parts);
    }

    private static string BuildCompatibility(ClassIslandThemePackage package)
    {
        if (package.Document is null)
            return package.SourcePath == "integrated"
                ? "状态：集成主题基线（未执行其 Avalonia 运行时代码）"
                : "状态：未能生成安全主题模型";

        var resources = package.Document.Resources.Values.Sum(x => x.Count);
        var styles = package.Document.Styles.Count;
        var warning = package.Diagnostics.Count;
        var source = package.IsArchive ? "ZIP 包" : "目录主题";
        return $"状态：已安全解析 {source} · 资源 {resources} · 样式 {styles} · 诊断 {warning}";
    }

    private static IReadOnlyList<ClassIslandThemePackage> GetDisplayPackages(ClassIslandThemeSnapshot snapshot)
    {
        var byId = snapshot.Packages
            .GroupBy(x => x.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(item => item.IsArchive).First(),
                StringComparer.OrdinalIgnoreCase);

        var result = new List<ClassIslandThemePackage>();
        foreach (var id in snapshot.EnabledThemeIds)
        {
            if (byId.Remove(id, out var package))
                result.Add(package);
        }

        result.AddRange(byId.Values.OrderBy(x => x.Manifest.Name, StringComparer.CurrentCultureIgnoreCase));
        return result;
    }

    private async Task ToggleAsync(string id, bool enable)
    {
        try
        {
            var enabled = store.ThemeSnapshot.EnabledThemeIds.ToList();
            var index = enabled.FindIndex(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (enable && index < 0)
                enabled.Add(id);
            else if (!enable && index >= 0)
                enabled.RemoveAt(index);
            else
                return;

            await store.UpdateEnabledThemesAsync(enabled);
            status.Text = enable ? $"已启用主题：{id}" : $"已禁用主题：{id}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            status.Text = $"无法更新主题状态：{exception.Message}";
        }
    }

    private async Task MoveAsync(string id, int delta)
    {
        try
        {
            var enabled = store.ThemeSnapshot.EnabledThemeIds.ToList();
            var index = enabled.FindIndex(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return;
            var target = Math.Clamp(index + delta, 0, enabled.Count - 1);
            if (target == index)
                return;
            (enabled[index], enabled[target]) = (enabled[target], enabled[index]);
            await store.UpdateEnabledThemesAsync(enabled);
            status.Text = $"已更新主题加载顺序：{id}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            status.Text = $"无法调整主题顺序：{exception.Message}";
        }
    }
}
