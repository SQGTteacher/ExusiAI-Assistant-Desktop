using System.IO;
using System.Text.Json;

namespace ExusiAI.Plugin.ClassIsland;

public sealed class ClassIslandProfileService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = null,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ClassIslandProfileService(string profilesDirectory)
    {
        ProfilesDirectory = Path.GetFullPath(profilesDirectory);
    }

    public string ProfilesDirectory { get; }
    public ClassIslandProfile? Current { get; private set; }
    public string? CurrentPath { get; private set; }
    public event EventHandler? ProfileChanged;

    public Guid AddClassPlanGroup(string name)
    {
        var profile = RequireCurrent();
        var id = Guid.NewGuid();
        profile.ClassPlanGroups.Add(id, new() { Name = name });
        ProfileChanged?.Invoke(this, EventArgs.Empty);
        return id;
    }

    public void DisbandClassPlanGroup(Guid id)
    {
        var profile = RequireCurrent();
        EnsureMutableGroup(id);
        foreach (var plan in profile.ClassPlans.Values.Where(x => x.AssociatedGroup == id))
            plan.AssociatedGroup = ClassIslandClassPlanGroup.DefaultGroupGuid;
        profile.ClassPlanGroups.Remove(id);
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteClassPlanGroup(Guid id)
    {
        var profile = RequireCurrent();
        EnsureMutableGroup(id);
        foreach (var key in profile.ClassPlans.Where(x => x.Value.AssociatedGroup == id).Select(x => x.Key).ToArray())
            profile.ClassPlans.Remove(key);
        profile.ClassPlanGroups.Remove(id);
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public Guid CreateTemporaryClassPlan(Guid sourcePlanId, Guid? timeLayoutId = null, DateTime? setupTime = null)
    {
        var profile = RequireCurrent();
        if (!profile.ClassPlans.TryGetValue(sourcePlanId, out var source)) throw new KeyNotFoundException("源课表不存在。");
        var clone = Clone(source);
        clone.IsOverlay = true;
        clone.OverlaySourceId = sourcePlanId;
        clone.OverlaySetupTime = setupTime ?? DateTime.Now;
        clone.TimeLayoutId = timeLayoutId ?? source.TimeLayoutId;
        clone.Name += "（临时层）";
        var id = Guid.NewGuid();
        profile.ClassPlans[id] = clone;
        profile.TempClassPlanId = id;
        profile.TempClassPlanSetupTime = clone.OverlaySetupTime;
        profile.OverlayClassPlanId = id;
        profile.IsOverlayClassPlanEnabled = true;
        ProfileChanged?.Invoke(this, EventArgs.Empty);
        return id;
    }

    public void ClearTemporaryClassPlan()
    {
        var profile = RequireCurrent();
        if (profile.TempClassPlanId is { } id) profile.ClassPlans.Remove(id);
        profile.TempClassPlanId = null;
        profile.OverlayClassPlanId = null;
        profile.IsOverlayClassPlanEnabled = false;
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(ProfilesDirectory);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Directory.EnumerateFiles(ProfilesDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task<ClassIslandProfile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = File.OpenRead(fullPath);
            var profile = await JsonSerializer.DeserializeAsync<ClassIslandProfile>(stream, jsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidDataException("ClassIsland 档案内容为空。");
            Validate(profile);
            Current = profile;
            CurrentPath = fullPath;
            ProfileChanged?.Invoke(this, EventArgs.Empty);
            return profile;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("ClassIsland 档案 JSON 无法解析。", exception);
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(ClassIslandProfile profile, string? path = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Validate(profile);
        var target = Path.GetFullPath(path ?? CurrentPath ?? Path.Combine(ProfilesDirectory, $"{profile.Id:N}.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ProfilesDirectory);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await JsonSerializer.SerializeAsync(stream, profile, jsonOptions, cancellationToken).ConfigureAwait(false);
                if (File.Exists(target)) File.Copy(target, target + ".bak", true);
                File.Move(temporary, target, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Current = profile;
            CurrentPath = target;
            ProfileChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { gate.Release(); }
    }

    public async Task<ClassIslandProfile> ImportAsync(Stream source, string? fileName = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ClassIslandProfile profile;
        try
        {
            profile = await JsonSerializer.DeserializeAsync<ClassIslandProfile>(source, jsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidDataException("ClassIsland 档案内容为空。");
        }
        catch (JsonException exception) { throw new InvalidDataException("ClassIsland 档案 JSON 无法解析。", exception); }
        Validate(profile);
        var safeName = string.IsNullOrWhiteSpace(fileName) ? $"{profile.Id:N}.json" : Path.GetFileName(fileName);
        if (!safeName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) safeName += ".json";
        await SaveAsync(profile, Path.Combine(ProfilesDirectory, safeName), cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public Task ExportAsync(ClassIslandProfile profile, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(destination);
        Validate(profile);
        return JsonSerializer.SerializeAsync(destination, profile, jsonOptions, cancellationToken);
    }

    private static void Validate(ClassIslandProfile profile)
    {
        profile.Subjects ??= [];
        profile.TimeLayouts ??= [];
        profile.ClassPlans ??= [];
        profile.ClassPlanGroups ??= [];
        profile.OrderedSchedules ??= [];
        profile.ScheduleItems ??= [];
        profile.Migrations ??= [];
        if (!profile.ClassPlanGroups.ContainsKey(ClassIslandClassPlanGroup.DefaultGroupGuid))
            profile.ClassPlanGroups[ClassIslandClassPlanGroup.DefaultGroupGuid] = new() { Name = "默认" };
        if (!profile.ClassPlanGroups.ContainsKey(Guid.Empty))
            profile.ClassPlanGroups[Guid.Empty] = new() { Name = "全局课表群", IsGlobal = true };
        foreach (var (id, plan) in profile.ClassPlans)
        {
            if (id == Guid.Empty) throw new InvalidDataException("ClassPlans 包含空 GUID。");
            if (plan.TimeLayoutId != Guid.Empty && !profile.TimeLayouts.ContainsKey(plan.TimeLayoutId))
                throw new InvalidDataException($"课表 {id} 引用了不存在的 TimeLayout {plan.TimeLayoutId}。");
        }
    }

    private ClassIslandProfile RequireCurrent() => Current ?? throw new InvalidOperationException("尚未加载 ClassIsland 档案。");
    private static void EnsureMutableGroup(Guid id)
    {
        if (id == Guid.Empty || id == ClassIslandClassPlanGroup.DefaultGroupGuid)
            throw new ArgumentException("不能删除或解散默认课表群和全局课表群。", nameof(id));
    }

    private ClassIslandClassPlan Clone(ClassIslandClassPlan source)
    {
        var json = JsonSerializer.Serialize(source, jsonOptions);
        return JsonSerializer.Deserialize<ClassIslandClassPlan>(json, jsonOptions)!;
    }
}
