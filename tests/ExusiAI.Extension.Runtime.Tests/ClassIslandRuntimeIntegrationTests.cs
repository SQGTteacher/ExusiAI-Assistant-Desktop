using System.IO;
using System.IO.Compression;
using System.Text;
using ExusiAI.Plugin.ClassIsland;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class ClassIslandRuntimeIntegrationTests
{
    [Fact]
    public void ProfileEnumsMatchMishaSerializedValues()
    {
        Assert.Equal(0, (int)ClassIslandTempClassPlanGroupType.Override);
        Assert.Equal(1, (int)ClassIslandTempClassPlanGroupType.Inherit);
        Assert.Equal(0, (int)ClassIslandScheduleType.Classic);
        Assert.Equal(1, (int)ClassIslandScheduleType.Schedule);
    }

    [Fact]
    public async Task ProfileRoundTripPreservesUnknownFieldsAndGuidMaps()
    {
        using var root = new TemporaryDirectory();
        var service = new ClassIslandProfileService(Path.Combine(root.Path, "Profiles"));
        await service.InitializeAsync();
        var subjectId = Guid.NewGuid();
        var layoutId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var json = $$"""
        {
          "Id": "{{Guid.NewGuid()}}",
          "Name": "一班",
          "FutureProfileField": { "Enabled": true },
          "Subjects": { "{{subjectId}}": { "Name": "语文", "FutureSubjectField": 42 } },
          "TimeLayouts": { "{{layoutId}}": { "Name": "默认", "Layouts": [{ "StartTime": "08:00:00", "EndTime": "08:40:00", "TimeType": 0, "FutureTimeField": "keep" }] } },
          "ClassPlans": { "{{planId}}": { "Name": "周一", "TimeLayoutId": "{{layoutId}}", "Classes": [{ "SubjectId": "{{subjectId}}" }], "TimeRule": { "WeekDay": 1, "WeekCountDiv": 1, "WeekCountDivTotal": 2, "FutureRule": [1,2] } } }
        }
        """;

        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var profile = await service.ImportAsync(input, "profile.json");
        Assert.True(profile.Subjects.ContainsKey(subjectId));
        Assert.True(profile.TimeLayouts.ContainsKey(layoutId));
        Assert.True(profile.ClassPlans.ContainsKey(planId));

        await using var output = new MemoryStream();
        await service.ExportAsync(profile, output);
        var roundTrip = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("FutureProfileField", roundTrip);
        Assert.Contains("FutureSubjectField", roundTrip);
        Assert.Contains("FutureTimeField", roundTrip);
        Assert.Contains("FutureRule", roundTrip);
    }

    [Fact]
    public async Task TimetableResolvesSubjectsLayoutsPlansAndRotatingWeekRules()
    {
        using var root = new TemporaryDirectory();
        var profiles = new ClassIslandProfileService(Path.Combine(root.Path, "Profiles"));
        await profiles.InitializeAsync();
        var subjectId = Guid.NewGuid();
        var layoutId = Guid.NewGuid();
        var profile = new ClassIslandProfile
        {
            Name = "测试",
            Subjects = { [subjectId] = new() { Name = "数学" } },
            TimeLayouts = { [layoutId] = new() { Layouts = { new() { StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(9) } } } },
            ClassPlans =
            {
                [Guid.NewGuid()] = new()
                {
                    TimeLayoutId = layoutId,
                    Classes = { new() { SubjectId = subjectId } },
                    TimeRule = new() { WeekDay = 1, WeekCountDiv = 2, WeekCountDivTotal = 2 }
                }
            }
        };
        await profiles.SaveAsync(profile);
        var timetable = new ClassIslandTimetableService(profiles);
        var anchor = new DateOnly(2026, 9, 21);

        Assert.Empty(timetable.GetLessons(new DateTime(2026, 9, 21, 8, 10, 0), anchor));
        var lessons = timetable.GetLessons(new DateTime(2026, 9, 28, 8, 10, 0), anchor);
        Assert.Single(lessons);
        Assert.Equal("数学", lessons[0].Subject.Name);
    }

    [Fact]
    public async Task ProfileServiceImplementsGroupsTemporaryOverlaysAndOrderedSchedules()
    {
        using var root = new TemporaryDirectory();
        var profiles = new ClassIslandProfileService(Path.Combine(root.Path, "Profiles"));
        await profiles.InitializeAsync();
        var layoutId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var profile = new ClassIslandProfile
        {
            TimeLayouts = { [layoutId] = new() },
            ClassPlans = { [planId] = new() { Name = "原课表", TimeLayoutId = layoutId } }
        };
        await profiles.SaveAsync(profile);
        var groupId = profiles.AddClassPlanGroup("测试组");
        profile.ClassPlans[planId].AssociatedGroup = groupId;
        profiles.DisbandClassPlanGroup(groupId);
        Assert.Equal(ClassIslandClassPlanGroup.DefaultGroupGuid, profile.ClassPlans[planId].AssociatedGroup);

        var overlayId = profiles.CreateTemporaryClassPlan(planId, setupTime: new DateTime(2026, 9, 27));
        Assert.True(profile.IsOverlayClassPlanEnabled);
        Assert.Equal(planId, profile.ClassPlans[overlayId].OverlaySourceId);
        profiles.ClearTemporaryClassPlan();
        Assert.False(profile.ClassPlans.ContainsKey(overlayId));
        Assert.False(profile.IsOverlayClassPlanEnabled);
    }

    [Fact]
    public async Task ComponentServiceUsesUpstreamProfileShapeAndCreatesBackup()
    {
        using var root = new TemporaryDirectory();
        var service = new ClassIslandComponentService(Path.Combine(root.Path, "ComponentLayouts"));
        await service.InitializeAsync();
        Assert.Single(service.CurrentComponents.Lines);
        Assert.Equal(2, service.CurrentComponents.Lines[0].Children.Count);
        service.CurrentComponents.Lines[0].IsMainLine = true;
        await service.SaveAsync();
        await service.SaveAsync();
        Assert.True(File.Exists(Path.Combine(root.Path, "ComponentLayouts", "Default.json.bak")));
        Assert.Contains("Default", service.ComponentConfigs);
    }

    [Fact]
    public async Task NotificationServiceRunsMaskAndOverlayLifecycle()
    {
        await using var service = new ClassIslandNotificationService();
        var request = new ClassIslandNotificationRequest
        {
            MaskContent = new() { Content = "mask", Duration = TimeSpan.FromMilliseconds(10) },
            OverlayContent = new() { Content = "overlay", Duration = TimeSpan.FromMilliseconds(10) }
        };
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        request.Completed += (_, _) => completed.TrySetResult();
        service.Start();
        service.Enqueue(request);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(ClassIslandNotificationState.Completed, request.State);
        Assert.True(request.MaskSession.IsCompleted);
        Assert.True(request.OverlaySession.IsCompleted);
    }

    [Fact]
    public void RuntimeDescriptorPinsExactUpstreamBaselines()
    {
        Assert.Equal("ClassIsland/ClassIsland", ClassIslandRuntimeDescriptor.UpstreamRepository);
        Assert.Equal("2.1.0.1", ClassIslandRuntimeDescriptor.RuntimeVersion);
        Assert.Equal("15273f82c9d2d55929df83b5fb806e68ee4547c0", ClassIslandRuntimeDescriptor.RuntimeReleaseCommit);
        Assert.Equal("develop/v2/misha-alpha", ClassIslandRuntimeDescriptor.MishaBranch);
        Assert.Equal("08808615899d1a4abb8e0ef576bf1e247adde10f", ClassIslandRuntimeDescriptor.MishaBaselineCommit);
        Assert.Equal("app-2.1.0.1-0", ClassIslandRuntimeDescriptor.AppFolderName);
    }

    [Fact]
    public void RuntimeLayoutMatchesNativeFolderLauncherContract()
    {
        var root = Path.Combine("root", "Runtime");
        Assert.Equal(Path.Combine(root, "ClassIsland.exe"), ClassIslandRuntimeDescriptor.LauncherPath(root));
        Assert.Equal(Path.Combine(root, "app-2.1.0.1-0", "ClassIsland.Desktop.exe"), ClassIslandRuntimeDescriptor.DesktopPath(root));
        Assert.Equal(Path.Combine(root, "data"), ClassIslandRuntimeDescriptor.DataDirectory(root));
    }

    [Fact]
    public async Task SyncReplacesOnlyManagedNativeEntries()
    {
        using var root = new TemporaryDirectory();
        var data = Path.Combine(root.Path, "data");
        Directory.CreateDirectory(Path.Combine(data, "Profiles"));
        Directory.CreateDirectory(Path.Combine(data, "Config"));
        Directory.CreateDirectory(Path.Combine(data, "Logs"));
        await File.WriteAllTextAsync(Path.Combine(data, "Settings.json"), "{\"old\":true}");
        await File.WriteAllTextAsync(Path.Combine(data, "Profiles", "old.json"), "old");
        await File.WriteAllTextAsync(Path.Combine(data, "Config", "old.json"), "old");
        await File.WriteAllTextAsync(Path.Combine(data, "Logs", "keep.log"), "keep");

        var archivePath = Path.Combine(root.Path, "Auto_Backup.zip");
        using (var file = File.Create(archivePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Write(archive, "backup/Settings.json", "{\"Theme\":2,\"FutureField\":true}");
            Write(archive, "backup/Profiles/profile.json", "{\"Name\":\"Class A\"}");
            Write(archive, "backup/Config/Components/default.json", "{\"Components\":[]}");
            Write(archive, "backup/Backups/ignored.zip", "ignored");
        }

        var summary = await ClassIslandBackupImporter.ImportIntoDataDirectoryAsync(archivePath, data);
        Assert.Equal(3, summary.TotalFileCount);
        Assert.Contains("FutureField", await File.ReadAllTextAsync(Path.Combine(data, "Settings.json")));
        Assert.True(File.Exists(Path.Combine(data, "Profiles", "profile.json")));
        Assert.False(File.Exists(Path.Combine(data, "Profiles", "old.json")));
        Assert.True(File.Exists(Path.Combine(data, "Config", "Components", "default.json")));
        Assert.False(File.Exists(Path.Combine(data, "Config", "old.json")));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(data, "Logs", "keep.log")));
    }

    [Fact]
    public void SyncRejectsTraversal()
    {
        using var root = new TemporaryDirectory();
        var archivePath = Path.Combine(root.Path, "bad.zip");
        using (var file = File.Create(archivePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Write(archive, "Settings.json", "{}");
            Write(archive, "../outside.txt", "bad");
        }
        Assert.Throws<InvalidDataException>(() => ClassIslandBackupImporter.Inspect(archivePath));
        Assert.False(File.Exists(Path.Combine(root.Path, "outside.txt")));
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "exusiai-classisland-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
