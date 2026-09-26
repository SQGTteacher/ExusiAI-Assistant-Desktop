using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Plugin.ArkPets;

public sealed class ArkPetsController : IAsyncDisposable
{
    private readonly IExtensionLogger logger;
    private readonly ArkPetsSettingsStore store = new();
    private readonly List<Process> processes = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private ClassIslandIntegrationBridge? classIslandBridge;

    public ArkPetsController(IExtensionLogger logger)
    {
        this.logger = logger;
    }

    public ArkPetsSettings Settings { get; private set; } = new();
    public ArkModelsCatalog Catalog { get; private set; } = ArkModelsCatalog.Empty();
    public bool ClassIslandAvailable =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExusiAI", "packages", "exusiai.misha-showcase", "package.json")) ||
        File.Exists(ClassIslandStateFile.DefaultPath);

    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Settings = await store.LoadAsync(cancellationToken);
        Settings = ApplyAutoDetection(Settings);
        if (!string.IsNullOrWhiteSpace(Settings.ModelRoot) && File.Exists(Path.Combine(Settings.ModelRoot, "models_data.json")))
            await ReloadModelsAsync(cancellationToken);
        else
            Changed?.Invoke(this, EventArgs.Empty);
        await store.SaveAsync(Settings, cancellationToken);
    }

    public void StartClassIslandBridge()
    {
        classIslandBridge ??= new ClassIslandIntegrationBridge(this, logger);
        classIslandBridge.Start();
    }

    public async Task UpdateSettingsAsync(
        Func<ArkPetsSettings, ArkPetsSettings> update,
        bool reloadModels = false,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Settings = update(Settings);
            await store.SaveAsync(Settings, cancellationToken);
            if (reloadModels)
                await ReloadModelsCoreAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ReloadModelsAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await ReloadModelsCoreAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SelectModelAsync(string? key, CancellationToken cancellationToken = default)
    {
        if (key is not null && Catalog.Models.All(x => !string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)))
            return;
        await UpdateSettingsAsync(x => x with { SelectedModelKey = key ?? "" }, cancellationToken: cancellationToken);
    }

    public async Task<ArkPetModel?> SelectRandomModelAsync(IEnumerable<ArkPetModel>? source = null, CancellationToken cancellationToken = default)
    {
        var candidates = (source ?? Catalog.Models).Where(x => x.IsAvailable).ToArray();
        if (candidates.Length == 0) return null;
        var model = candidates[Random.Shared.Next(candidates.Length)];
        await SelectModelAsync(model.Key, cancellationToken);
        return model;
    }

    public ArkPetModel? SelectedModel =>
        Catalog.Models.FirstOrDefault(x => string.Equals(x.Key, Settings.SelectedModelKey, StringComparison.OrdinalIgnoreCase));

    public async Task<Process> LaunchSelectedAsync(CancellationToken cancellationToken = default)
    {
        var model = SelectedModel ?? throw new InvalidOperationException("请先选择一个桌宠模型。");
        if (!model.IsAvailable)
            throw new FileNotFoundException("所选模型文件不完整，请重新导入或更新 Ark-Models 模型库。");
        if (string.IsNullOrWhiteSpace(Settings.RuntimePath) || !File.Exists(Settings.RuntimePath))
            throw new FileNotFoundException("尚未设置可用的 ArkPets.exe 或 ArkPets.jar。", Settings.RuntimePath);
        if (string.IsNullOrWhiteSpace(Settings.ModelRoot) || !Directory.Exists(Settings.ModelRoot))
            throw new DirectoryNotFoundException("Ark-Models 模型库目录不可用。");

        var configPath = await ArkPetsConfigWriter.WriteAsync(Settings, Catalog, model, cancellationToken);
        var info = BuildProcessStartInfo(Settings.RuntimePath, Settings.ModelRoot, configPath);
        var process = Process.Start(info) ?? throw new InvalidOperationException("ArkPets 运行时未能启动。");
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            lock (processes)
            {
                processes.Remove(process);
            }
            process.Dispose();
        };
        lock (processes)
        {
            processes.Add(process);
        }
        logger.Information($"Started ArkPets model '{model.Key}'.");
        return process;
    }

    public void StopAll()
    {
        Process[] snapshot;
        lock (processes)
            snapshot = processes.ToArray();

        foreach (var process in snapshot)
        {
            try
            {
                if (!process.HasExited)
                    process.CloseMainWindow();
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
            {
                logger.Warning($"Could not request ArkPets process shutdown: {exception.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopAll();
        if (classIslandBridge is not null)
        {
            await classIslandBridge.DisposeAsync();
            classIslandBridge = null;
        }
        gate.Dispose();
    }

    private async Task ReloadModelsCoreAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Settings.ModelRoot))
        {
            Catalog = ArkModelsCatalog.Empty();
            return;
        }

        Catalog = await ArkModelsDataset.LoadAsync(Settings.ModelRoot, cancellationToken);
        if (!string.IsNullOrWhiteSpace(Settings.SelectedModelKey) &&
            Catalog.Models.Any(x => string.Equals(x.Key, Settings.SelectedModelKey, StringComparison.OrdinalIgnoreCase)))
            return;

        var exusiai = Catalog.Models.FirstOrDefault(x =>
            x.Key.Contains("103_angel", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("能天使", StringComparison.CurrentCultureIgnoreCase) ||
            x.Appellation.Equals("Exusiai", StringComparison.OrdinalIgnoreCase));
        Settings = Settings with { SelectedModelKey = exusiai?.Key ?? Catalog.Models.FirstOrDefault()?.Key ?? "" };
        await store.SaveAsync(Settings, cancellationToken);
    }

    private static ProcessStartInfo BuildProcessStartInfo(string runtimePath, string modelRoot, string configPath)
    {
        var isJar = string.Equals(Path.GetExtension(runtimePath), ".jar", StringComparison.OrdinalIgnoreCase);
        var info = new ProcessStartInfo
        {
            FileName = isJar ? "java" : runtimePath,
            WorkingDirectory = modelRoot,
            UseShellExecute = false
        };
        if (isJar)
        {
            info.ArgumentList.Add("-jar");
            info.ArgumentList.Add(runtimePath);
        }
        info.ArgumentList.Add("--direct-start");
        info.ArgumentList.Add("--config");
        info.ArgumentList.Add(configPath);
        return info;
    }

    private static ArkPetsSettings ApplyAutoDetection(ArkPetsSettings settings)
    {
        var runtime = settings.RuntimePath;
        if (string.IsNullOrWhiteSpace(runtime) || !File.Exists(runtime))
            runtime = ArkPetsRuntimeLocator.FindRuntime() ?? "";

        var modelRoot = settings.ModelRoot;
        if (string.IsNullOrWhiteSpace(modelRoot) || !File.Exists(Path.Combine(modelRoot, "models_data.json")))
        {
            var nearRuntime = !string.IsNullOrWhiteSpace(runtime) ? Path.GetDirectoryName(runtime) : null;
            modelRoot = ArkPetsRuntimeLocator.FindModelRoot(nearRuntime) ?? "";
        }

        return settings with { RuntimePath = runtime, ModelRoot = modelRoot };
    }
}

internal static class ArkPetsRuntimeLocator
{
    public static string? FindRuntime()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "arkpets", "ArkPets.exe"),
            Path.Combine(local, "Programs", "ArkPets", "ArkPets.exe"),
            Path.Combine(local, "ArkPets", "ArkPets.exe"),
            Path.Combine(programFiles, "ArkPets", "ArkPets.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string? FindModelRoot(string? nearRuntime)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            nearRuntime,
            Path.Combine(local, "ArkPets"),
            Path.Combine(local, "Programs", "ArkPets"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ArkPets")
        };
        return candidates.Where(x => !string.IsNullOrWhiteSpace(x))
            .FirstOrDefault(x => File.Exists(Path.Combine(x!, "models_data.json")));
    }
}

public static class ArkPetsConfigWriter
{
    public static async Task<string> WriteAsync(
        ArkPetsSettings settings,
        ArkModelsCatalog catalog,
        ArkPetModel model,
        CancellationToken cancellationToken = default)
    {
        var configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExusiAI", "arkpets", "configs");
        Directory.CreateDirectory(configDirectory);
        var configPath = Path.Combine(configDirectory, $"pet-{Sanitize(model.Key)}.json");
        var relativeAsset = Path.GetRelativePath(catalog.RootDirectory, model.AssetDirectory).Replace('\\', '/');

        var files = new JsonObject();
        foreach (var pair in model.AssetFiles)
        {
            files[pair.Key] = pair.Value.Count == 1
                ? JsonValue.Create(pair.Value[0])
                : new JsonArray(pair.Value.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        }

        var root = new JsonObject
        {
            ["behavior_ai_activation"] = settings.BehaviorAiActivation,
            ["behavior_allow_interact"] = settings.BehaviorAllowInteract,
            ["behavior_allow_sit"] = settings.BehaviorAllowSit,
            ["behavior_allow_sleep"] = settings.BehaviorAllowSleep,
            ["behavior_allow_special"] = settings.BehaviorAllowSpecial,
            ["behavior_allow_walk"] = settings.BehaviorAllowWalk,
            ["behavior_direction_switching"] = settings.BehaviorDirectionSwitching,
            ["behavior_do_peer_repulsion"] = settings.BehaviorDoPeerRepulsion,
            ["behavior_walk_speed"] = settings.BehaviorWalkSpeed,
            ["canvas_color"] = "#00000000",
            ["canvas_coverage"] = settings.CanvasCoverage,
            ["canvas_sampling_interval"] = settings.CanvasSamplingInterval,
            ["character_asset"] = relativeAsset,
            ["character_favorites"] = new JsonObject(),
            ["character_files"] = files,
            ["character_label"] = model.Name,
            ["display_fps"] = settings.DisplayFps,
            ["display_margin_bottom"] = settings.DisplayMarginBottom,
            ["display_multi_monitors"] = settings.DisplayMultiMonitors,
            ["display_scale"] = settings.DisplayScale,
            ["download_mc_cdk"] = "",
            ["eco_mode"] = settings.EcoMode,
            ["enable_telemetry"] = false,
            ["initial_position_x"] = 0.2,
            ["initial_position_y"] = 0.2,
            ["launcher_solid_exit"] = true,
            ["logging_level"] = settings.LoggingLevel,
            ["opacity_dim"] = settings.OpacityDim,
            ["opacity_normal"] = settings.OpacityNormal,
            ["physic_air_friction_acc"] = settings.PhysicAirFrictionAcc,
            ["physic_gravity_acc"] = settings.PhysicGravityAcc,
            ["physic_speed_limit_x"] = settings.PhysicSpeedLimitX,
            ["physic_speed_limit_y"] = settings.PhysicSpeedLimitY,
            ["physic_static_friction_acc"] = settings.PhysicStaticFrictionAcc,
            ["render_animation_mixture"] = settings.RenderAnimationMixture,
            ["render_enable_mipmap"] = settings.RenderEnableMipmap,
            ["render_outline"] = settings.RenderOutline,
            ["render_outline_color"] = settings.RenderOutlineColor,
            ["render_outline_emphasis"] = 3,
            ["render_outline_emphasis_color"] = settings.RenderOutlineEmphasisColor,
            ["render_outline_width"] = settings.RenderOutlineWidth,
            ["render_shader_high_quality"] = settings.RenderShaderHighQuality,
            ["render_shadow_color"] = settings.RenderShadowColor,
            ["transition_duration"] = 0.3,
            ["transition_type"] = "EASE_OUT_CUBIC",
            ["user_announcement_read"] = new JsonObject(),
            ["window_style_toolwindow"] = settings.WindowStyleToolwindow,
            ["window_style_topmost"] = settings.WindowStyleTopmost
        };

        var temporary = configPath + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
        File.Move(temporary, configPath, true);
        return configPath;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "pet" : result;
    }
}
