using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using ExusiAI.Plugin.ClassIsland;
using ExusiAI.Extension.Abstractions;

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
    public async Task ProfileRoundTripKeepsGuidAttachedSettingsAndLegacySubjectFields()
    {
        using var root = new TemporaryDirectory();
        var service = new ClassIslandProfileService(Path.Combine(root.Path, "Profiles"));
        await service.InitializeAsync();
        var subjectId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var json = $$"""
        { "Subjects": { "{{subjectId}}": { "Name": "物理", "Location": "旧版字段",
          "AttachedObjects": { "{{attachmentId}}": { "Enabled": true, "Rule": { "Version": 2 } } } } } }
        """;
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var profile = await service.ImportAsync(input, "sample.json");
        var attachment = profile.Subjects[subjectId].GetAttachedObject<System.Text.Json.JsonElement>(attachmentId);
        Assert.True(attachment.GetProperty("Enabled").GetBoolean());
        Assert.True(profile.Subjects[subjectId].ExtensionData.ContainsKey("Location"));
        await using var output = new MemoryStream();
        await service.ExportAsync(profile, output);
        using var document = System.Text.Json.JsonDocument.Parse(output.ToArray());
        var subject = document.RootElement.GetProperty("Subjects").GetProperty(subjectId.ToString());
        Assert.Equal("旧版字段", subject.GetProperty("Location").GetString());
        Assert.Equal(2, subject.GetProperty("AttachedObjects").GetProperty(attachmentId.ToString())
            .GetProperty("Rule").GetProperty("Version").GetInt32());
    }

    [Fact]
    public void ClassNotificationAttachmentUsesUpstreamGuidAndPreservesUnknownSettings()
    {
        var subject = new ClassIslandSubject();
        var id = ClassIslandClassNotificationAttachedSettings.Id;
        Assert.Equal(new Guid("08F0D9C3-C770-4093-A3D0-02F3D90C24BC"), id);
        subject.AttachedObjects[id] = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            IsAttachSettingsEnabled = true, ClassPreparingDeltaTime = 90, FutureOption = "keep"
        });
        var settings = subject.GetAttachedObject<ClassIslandClassNotificationAttachedSettings>(id)!;
        Assert.True(settings.IsAttachSettingsEnabled);
        Assert.Equal(90, settings.ClassPreparingDeltaTime);
        settings.ClassOnMaskText = "开课";
        subject.SetAttachedObject(id, settings);
        var saved = subject.AttachedObjects[id];
        Assert.Equal("keep", saved.GetProperty("FutureOption").GetString());
        Assert.Equal("开课", saved.GetProperty("ClassOnMaskText").GetString());
    }

    [Fact]
    public async Task ComponentServiceLoadsSelectedConfiguration()
    {
        using var root = new TemporaryDirectory();
        var directory = Path.Combine(root.Path, "Config", "ComponentLayouts");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Default.json"), "{\"Lines\":[]}");
        await File.WriteAllTextAsync(Path.Combine(directory, "Teaching.json"), "{\"Lines\":[]}");
        var service = new ClassIslandComponentService(directory);
        await service.InitializeAsync("Teaching");
        Assert.Equal("Teaching", service.CurrentConfigName);
    }

    [Fact]
    public async Task CoreRestoresSettingsSelectedProfileAndLayout()
    {
        using var root = new TemporaryDirectory();
        var profiles = Path.Combine(root.Path, "Profiles");
        var layouts = Path.Combine(root.Path, "Config", "ComponentLayouts");
        Directory.CreateDirectory(profiles);
        Directory.CreateDirectory(layouts);
        await File.WriteAllTextAsync(Path.Combine(profiles, "a.json"), "{\"Name\":\"first\"}");
        await File.WriteAllTextAsync(Path.Combine(profiles, "b.json"), "{\"Name\":\"selected\"}");
        await File.WriteAllTextAsync(Path.Combine(layouts, "Default.json"), "{\"Lines\":[]}");
        await File.WriteAllTextAsync(Path.Combine(layouts, "Teaching.json"), "{\"Lines\":[]}");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Settings.json"),
            "{\"SelectedProfile\":\"b.json\",\"CurrentComponentConfig\":\"Teaching\"}");
        await using var core = new ClassIslandCoreService(new NullExtensionLogger(), root.Path);
        await core.InitializeAsync();
        Assert.Equal("selected", core.Profiles.Current?.Name);
        Assert.Equal("Teaching", core.Components.CurrentConfigName);
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
        timetable.RotationAnchor = anchor;
        Assert.Single(timetable.GetLessons(new DateTime(2026, 9, 28, 8, 10, 0)));
    }

    [Fact]
    public async Task GeneralSettingsPreserveUpstreamFieldsAndRotationAnchor()
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Settings.json"),
            "{\"SingleWeekStartTime\":\"2026-09-21T00:00:00\",\"UnknownSetting\":{\"Keep\":true}}");
        var settings = new ClassIslandSettingsService(root.Path);
        await settings.LoadAsync();
        Assert.Equal(new DateOnly(2026, 9, 21), settings.SingleWeekStartTime);
        await settings.SaveGeneralAsync(new DateOnly(2026, 9, 28), 3.5, "ntp.aliyun.com");
        using var saved = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root.Path, "Settings.json")));
        Assert.True(saved.RootElement.GetProperty("UnknownSetting").GetProperty("Keep").GetBoolean());
        Assert.Equal(3.5, saved.RootElement.GetProperty("TimeOffsetSeconds").GetDouble());
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
    public void ComponentSettingsEditPreservesUnknownCountdownFields()
    {
        var component = new ClassIslandComponentSettings
        {
            Settings = System.Text.Json.JsonSerializer.SerializeToElement(new
            {
                CountDownName = "旧名称",
                FutureSetting = new { Enabled = true }
            })
        };
        component.UpdateSettings(values =>
        {
            values["CountDownName"] = "新名称";
            values["ShowProgress"] = true;
        });
        var saved = component.Settings!.Value;
        Assert.Equal("新名称", saved.GetProperty("CountDownName").GetString());
        Assert.True(saved.GetProperty("ShowProgress").GetBoolean());
        Assert.True(saved.GetProperty("FutureSetting").GetProperty("Enabled").GetBoolean());
    }

    [Theory]
    [InlineData("#FF0000FF", 255, 255, 0, 0)]
    [InlineData("#0080FF80", 128, 0, 128, 255)]
    [InlineData("#336699", 255, 51, 102, 153)]
    public void ImportedCountdownColorUsesTrailingAlpha(string hex, byte expectedAlpha,
        byte expectedRed, byte expectedGreen, byte expectedBlue)
    {
        Assert.True(ClassIslandComponentText.TryParseUpstreamColor(hex,
            out var alpha, out var red, out var green, out var blue));
        Assert.Equal((expectedAlpha, expectedRed, expectedGreen, expectedBlue), (alpha, red, green, blue));
        Assert.False(ClassIslandComponentText.TryParseUpstreamColor("#XYZ", out _, out _, out _, out _));
    }

    [Fact]
    public void WeeklyCountdownPreservesEightDayFallback()
    {
        var settings = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            CountdownSource = 3,
            WeekCountdownStartDay = 1
        });
        var monday = new DateTime(2026, 9, 28, 12, 0, 0);
        var tuesday = monday.AddDays(1);
        Assert.True(ClassIslandComponentText.TryGetCountdownWindow(settings, monday, out var start, out var end));
        Assert.Equal(new DateTime(2026, 9, 28), start);
        Assert.Equal(new DateTime(2026, 10, 6), end);
        Assert.True(ClassIslandComponentText.TryGetCountdownWindow(settings, tuesday, out var nextStart, out var nextEnd));
        Assert.Equal(start, nextStart);
        Assert.Equal(new DateTime(2026, 10, 7), nextEnd);
    }

    [Fact]
    public async Task DailyCountdownUsesTheFirstAndLastTimetableEntries()
    {
        using var root = new TemporaryDirectory();
        var profiles = new ClassIslandProfileService(Path.Combine(root.Path, "Profiles"));
        await profiles.InitializeAsync();
        var layoutId = Guid.NewGuid();
        var profile = new ClassIslandProfile
        {
            TimeLayouts = { [layoutId] = new()
            {
                Layouts =
                {
                    new() { TimeType = 0, StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(8.75) },
                    new() { TimeType = 1, StartTime = TimeSpan.FromHours(9), EndTime = TimeSpan.FromHours(10) }
                }
            } },
            ClassPlans = { [Guid.NewGuid()] = new()
            {
                TimeLayoutId = layoutId, TimeRule = new() { WeekDay = 1 }
            } }
        };
        await profiles.SaveAsync(profile);
        var timetable = new ClassIslandTimetableService(profiles);
        var monday = new DateTime(2026, 9, 28, 9, 0, 0);
        var settings = System.Text.Json.JsonSerializer.SerializeToElement(new { CountdownSource = 2 });
        Assert.True(ClassIslandComponentText.TryGetCountdownWindow(settings, monday, timetable, out var start, out var end));
        Assert.Equal(monday.Date.AddHours(8), start);
        Assert.Equal(monday.Date.AddHours(10), end);
        var natural = System.Text.Json.JsonSerializer.SerializeToElement(new { CountdownSource = 2, NatureTimeUseMode = 1 });
        Assert.True(ClassIslandComponentText.TryGetCountdownWindow(natural, monday, timetable, out start, out end));
        Assert.Equal(monday.Date, start);
        Assert.Equal(monday.Date.AddDays(1), end);
    }

    [Fact]
    public async Task EmbeddedHostUsesPluginPackageDirectory()
    {
        using var root = new TemporaryDirectory();
        var package = Path.Combine(root.Path, "plugin");
        var native = Path.Combine(package, "NativeClassIsland");
        Directory.CreateDirectory(native);
        await File.WriteAllTextAsync(Path.Combine(native, "ClassIsland.Desktop.dll"), "");
        await File.WriteAllTextAsync(Path.Combine(native, "ClassIsland.dll"), "");
        await using var core = new ClassIslandCoreService(new NullExtensionLogger(),
            Path.Combine(root.Path, "data"), package);
        Assert.True(core.Presentation.IsOriginalHostAvailable);
    }

    [Fact]
    public async Task WeeklyCountdownUsesCourseTimeAndForcedMode()
    {
        using var root = new TemporaryDirectory();
        var profiles = new ClassIslandProfileService(Path.Combine(root.Path, "Profiles"));
        await profiles.InitializeAsync();
        var layoutId = Guid.NewGuid();
        var profile = new ClassIslandProfile
        {
            TimeLayouts = { [layoutId] = new()
            {
                Layouts = { new() { TimeType = 0, StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(9) } }
            } },
            ClassPlans = { [Guid.NewGuid()] = new()
            {
                TimeLayoutId = layoutId, TimeRule = new() { WeekDay = 1 }
            } }
        };
        await profiles.SaveAsync(profile);
        var timetable = new ClassIslandTimetableService(profiles);
        var monday = new DateTime(2026, 9, 28, 12, 0, 0);
        var settings = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            CountdownSource = 3, WeekCountdownStartDay = 1
        });
        Assert.True(ClassIslandComponentText.TryGetCountdownWindow(settings, monday, timetable, out var start, out var end));
        Assert.Equal(monday.Date.AddHours(8), start);
        Assert.Equal(monday.Date.AddHours(9), end);

        var natural = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            CountdownSource = 3, WeekCountdownStartDay = 1, NatureTimeUseMode = 1
        });
        Assert.True(ClassIslandComponentText.TryGetCountdownWindow(natural, monday, timetable, out start, out end));
        Assert.Equal(monday.Date, start);
        Assert.Equal(monday.Date.AddDays(8), end);

        var forced = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            CountdownSource = 3, WeekCountdownStartDay = 1, NatureTimeUseMode = 2
        });
        Assert.True(ClassIslandComponentText.TryGetCountdownWindow(forced, monday.AddDays(1),
            new ClassIslandTimetableService(new ClassIslandProfileService(Path.Combine(root.Path, "empty"))),
            out start, out end));
        Assert.Equal(DateTime.MinValue, start);
        Assert.Equal(start, end);
    }

    [Fact]
    public void LegacyCountdownColorObjectRetainsChannels()
    {
        var settings = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            FontColor = new { A = 128, R = 255, G = 32, B = 0 }
        });
        Assert.True(ClassIslandComponentText.TryReadCountdownColor(settings,
            out var alpha, out var red, out var green, out var blue));
        Assert.Equal((128, 255, 32, 0), ((int)alpha, (int)red, (int)green, (int)blue));
        var component = new ClassIslandComponentSettings { Settings = settings };
        component.UpdateSettings(values => values["CountDownName"] = "编辑后");
        Assert.Equal(System.Text.Json.JsonValueKind.Object, component.Settings!.Value.GetProperty("FontColor").ValueKind);
    }

    [Fact]
    public void CyclicCountdownUsesConfiguredDurations()
    {
        var settings = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            CountdownSource = 1,
            CycleStartTime = "2026-09-28T08:00:00",
            CycleDuration = "01:00:00",
            IsAdvancedCycleTimingEnabled = true,
            CycleBeforeDuration = "00:10:00",
            CycleAfterDuration = "00:20:00"
        });
        Assert.True(ClassIslandComponentText.TryGetCountdownWindow(settings,
            new DateTime(2026, 9, 28, 9, 40, 0), out var start, out var end));
        Assert.Equal(new DateTime(2026, 9, 28, 9, 40, 0), start);
        Assert.Equal(new DateTime(2026, 9, 28, 10, 40, 0), end);
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
    public async Task AppearanceSettingsClampRadiusAndPersistDockPosition()
    {
        using var root = new TemporaryDirectory();
        var service = new ClassIslandAppearanceService(root.Path);
        service.Settings.Height = 60;
        service.Settings.CornerRadius = 100;
        service.Settings.DockPosition = ClassIslandDockPosition.BottomRight;
        await service.SaveAsync();
        var reloaded = new ClassIslandAppearanceService(root.Path);
        await reloaded.LoadAsync();
        Assert.Equal(30, reloaded.Settings.CornerRadius);
        Assert.Equal(ClassIslandDockPosition.BottomRight, reloaded.Settings.DockPosition);
    }

    [Fact]
    public async Task AppearanceMigratesUntouchedWidePresetToCompactIsland()
    {
        using var root = new TemporaryDirectory();
        var config = Path.Combine(root.Path, "Config");
        Directory.CreateDirectory(config);
        await File.WriteAllTextAsync(Path.Combine(config, "Appearance.json"),
            "{\"Width\":620,\"Height\":72,\"CornerRadius\":28}");
        var appearance = new ClassIslandAppearanceService(root.Path);
        await appearance.LoadAsync();
        Assert.Equal(440, appearance.Settings.Width);
        Assert.Equal(52, appearance.Settings.Height);
        Assert.True(appearance.Settings.FadeOnPointerEnter);
        appearance.Settings.IslandTheme = ClassIslandIslandTheme.LightGlass;
        appearance.Settings.HoverOpacity = 0.12;
        await appearance.SaveAsync();
        var reloaded = new ClassIslandAppearanceService(root.Path);
        await reloaded.LoadAsync();
        Assert.Equal(ClassIslandIslandTheme.LightGlass, reloaded.Settings.IslandTheme);
        Assert.Equal(0.12, reloaded.Settings.HoverOpacity);
        Assert.Equal(440, reloaded.Settings.Width);
    }

    [Fact]
    public async Task AppearanceLeavesCustomWidthUntouched()
    {
        using var root = new TemporaryDirectory();
        var config = Path.Combine(root.Path, "Config");
        Directory.CreateDirectory(config);
        await File.WriteAllTextAsync(Path.Combine(config, "Appearance.json"),
            "{\"Width\":500,\"Height\":72}");
        var appearance = new ClassIslandAppearanceService(root.Path);
        await appearance.LoadAsync();
        Assert.Equal(500, appearance.Settings.Width);
    }

    [Fact]
    public void WindowPlacementKeepsDraggableAreaVisibleAcrossMonitorBounds()
    {
        var secondary = new Rect(-1920, 0, 1920, 1040);
        var size = new Size(620, 72);
        var position = ClassIslandWindowPlacement.Clamp(new Point(-2500, -100), size, secondary);
        Assert.Equal(-2492, position.X);
        Assert.Equal(0, position.Y);
        var right = ClassIslandWindowPlacement.Clamp(new Point(1000, 2000), size, secondary);
        Assert.Equal(-48, right.X);
        Assert.Equal(992, right.Y);
    }

    [Fact]
    public async Task CancelledNotificationDoesNotComplete()
    {
        await using var service = new ClassIslandNotificationService();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new ClassIslandNotificationRequest
        {
            MaskContent = new() { Content = "取消", Duration = TimeSpan.FromSeconds(5) }
        };
        service.RequestStarted += (_, _) => started.TrySetResult();
        service.RequestCompleted += (_, _) => finished.TrySetResult();
        service.Start();
        service.Enqueue(request);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        request.Cancel();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(ClassIslandNotificationState.Cancelled, request.State);
        Assert.False(request.CompletedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task NotificationCancelledWhileQueuedNeverStarts()
    {
        await using var service = new ClassIslandNotificationService();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = false;
        var request = new ClassIslandNotificationRequest();
        service.RequestStarted += (_, _) => started = true;
        service.RequestCompleted += (_, completed) =>
        {
            if (ReferenceEquals(completed, request)) finished.TrySetResult();
        };
        service.Enqueue(request);
        request.Cancel();
        service.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(started);
        Assert.Equal(ClassIslandNotificationState.Cancelled, request.State);
        Assert.False(request.CompletedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task HostStopAndRestartResumesNotification()
    {
        await using var service = new ClassIslandNotificationService();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new ClassIslandNotificationRequest
        {
            MaskContent = new() { Content = "继续", Duration = TimeSpan.FromMilliseconds(150) }
        };
        service.RequestStarted += (_, _) => started.TrySetResult();
        request.Completed += (_, _) => completed.TrySetResult();
        service.Start();
        service.Enqueue(request);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync();
        Assert.Equal(ClassIslandNotificationState.Queued, request.State);
        service.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(ClassIslandNotificationState.Completed, request.State);
    }

    [Fact]
    public void ComponentCatalogUsesUpstreamMishaGuids()
    {
        Assert.Equal("日期", ClassIslandComponentCatalog.Find("DF3F8295-21F6-482E-BADA-FA0E5F14BB66")?.Name);
        Assert.Equal("课程表", ClassIslandComponentCatalog.Find("1DB2017D-E374-4BC6-9D57-0B4ADF03A6B8")?.Name);
        Assert.Equal("时钟", ClassIslandComponentCatalog.Find("9E1AF71D-8F77-4B21-A342-448787104DD9")?.Name);
        Assert.Equal(11, ClassIslandComponentCatalog.BuiltIn.Count);
    }

    [Fact]
    public void BuiltInComponentTextReadsUpstreamSettings()
    {
        using var root = new TemporaryDirectory();
        var timetable = new ClassIslandTimetableService(new ClassIslandProfileService(Path.Combine(root.Path, "Profiles")));
        var now = new DateTime(2026, 10, 1, 9, 30, 15);
        var clock = new ClassIslandComponentSettings
        {
            Id = "9E1AF71D-8F77-4B21-A342-448787104DD9",
            Settings = System.Text.Json.JsonSerializer.SerializeToElement(new { ShowSeconds = true })
        };
        Assert.Equal("09:30:15", ClassIslandComponentText.Resolve(clock, timetable, now));
        var countdown = new ClassIslandComponentSettings
        {
            Id = "7C645D35-8151-48BA-B4AC-15017460D994",
            Settings = System.Text.Json.JsonSerializer.SerializeToElement(new
            {
                CountDownName = "高考", CountDownConnector = "还有", OverTime = "2026-10-11T00:00:00"
            })
        };
        Assert.Equal("距离 高考 还有 10天", ClassIslandComponentText.Resolve(countdown, timetable, now));
        countdown.Settings = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            CountDownName = "高考", OverTime = "2026-10-11T00:00:00", IsCompactModeEnabled = true,
            CustomStringFormat = "%D天 %h小时"
        });
        Assert.Equal("高考 10天 14小时", ClassIslandComponentText.Resolve(countdown, timetable, now));
        clock.Settings = System.Text.Json.JsonSerializer.SerializeToElement(new { ShowSeconds = false });
        Assert.Equal("09 30", ClassIslandComponentText.Resolve(clock, timetable, new DateTime(2026, 10, 1, 9, 30, 14)));
        countdown.Settings = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            CountDownName = "课间", CountdownSource = 1, CycleStartTime = "2026-10-01T08:00:00",
            CycleDuration = "00:40:00", CycleBeforeDuration = "00:05:00", CycleAfterDuration = "00:05:00",
            IsAdvancedCycleTimingEnabled = true, CustomStringFormat = "%m:%s"
        });
        Assert.Equal("距离 课间 还有 39:45", ClassIslandComponentText.Resolve(countdown, timetable, new DateTime(2026, 10, 1, 8, 55, 15)));
        countdown.Settings = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            CountDownName = "课间", CountdownSource = 1, CycleStartTime = "2026-10-01T08:00:00",
            CycleDuration = "00:40:00", IsCycleCountLimited = true, CycleCountLimit = 1,
            CustomStringFormat = "%m:%s"
        });
        Assert.Equal("距离 课间 还有 00:00", ClassIslandComponentText.Resolve(countdown, timetable, new DateTime(2026, 10, 1, 10, 0, 0)));
        var weather = new ClassIslandComponentSettings
        {
            Id = "CA495086-E297-4BEB-9603-C5C1C1A8551E",
            Settings = System.Text.Json.JsonSerializer.SerializeToElement(new { MainWeatherInfoKind = 0 })
        };
        var snapshot = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            current = new { temperature = new { value = "22", unit = "°C" } },
            alerts = new[] { new { title = "大风预警" } }
        });
        Assert.Equal("22°C  大风预警", ClassIslandComponentText.Resolve(weather, timetable, now, snapshot));
        var group = new ClassIslandComponentSettings
        {
            Id = "C911D762-107F-40C6-84CC-0146AB3C86B1",
            Settings = System.Text.Json.JsonSerializer.SerializeToElement(new
            {
                Children = new[] { new { Id = "EE8F66BD-C423-4E7C-AB46-AA9976B00E08", Settings = new { TextContent = "测试" } } }
            })
        };
        Assert.Equal("测试", ClassIslandComponentText.Resolve(group, timetable, now));
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
    public async Task InstalledDataImportCopiesWorkingSetWithoutChangingOriginal()
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "Original");
        var destination = Path.Combine(root.Path, "Plugin");
        Directory.CreateDirectory(Path.Combine(source, "Profiles"));
        Directory.CreateDirectory(Path.Combine(source, "Config", "ComponentLayouts"));
        await File.WriteAllTextAsync(Path.Combine(source, "Settings.json"), "{\"CurrentComponentConfig\":\"Default\"}");
        await File.WriteAllTextAsync(Path.Combine(source, "Profiles", "a.json"), "{\"Name\":\"一班\"}");
        await File.WriteAllTextAsync(Path.Combine(source, "Config", "ComponentLayouts", "Default.json"), "{\"Lines\":[]}");
        await using var core = new ClassIslandCoreService(new NullExtensionLogger(), destination);
        await core.InitializeAsync();
        var summary = await core.ImportDataDirectoryAsync(source);
        Assert.Equal(3, summary.TotalFileCount);
        Assert.True(File.Exists(Path.Combine(destination, "Profiles", "a.json")));
        Assert.Equal("{\"Name\":\"一班\"}", await File.ReadAllTextAsync(Path.Combine(source, "Profiles", "a.json")));
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

    [Fact]
    public void SyncRejectsDuplicatePathsIgnoringCase()
    {
        using var root = new TemporaryDirectory();
        var archivePath = Path.Combine(root.Path, "duplicate.zip");
        using (var file = File.Create(archivePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Write(archive, "Settings.json", "{}");
            Write(archive, "Profiles/class.json", "{}");
            Write(archive, "profiles/CLASS.json", "{}");
        }
        Assert.Throws<InvalidDataException>(() => ClassIslandBackupImporter.Inspect(archivePath));
    }

    [Fact]
    public void SyncRejectsUnixSymbolicLinks()
    {
        using var root = new TemporaryDirectory();
        var archivePath = Path.Combine(root.Path, "symlink.zip");
        using (var file = File.Create(archivePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Write(archive, "Settings.json", "{}");
            var link = archive.CreateEntry("Profiles/link.json");
            link.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var writer = new StreamWriter(link.Open(), Encoding.UTF8);
            writer.Write("../../outside.json");
        }
        Assert.Throws<InvalidDataException>(() => ClassIslandBackupImporter.Inspect(archivePath));
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private sealed class NullExtensionLogger : IExtensionLogger
    {
        public void Information(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
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
