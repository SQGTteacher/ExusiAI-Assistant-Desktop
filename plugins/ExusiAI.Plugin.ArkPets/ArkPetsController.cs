using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Plugin.ArkPets;

public sealed record ArkPetProcessSnapshot(
    Guid Id,
    int ProcessId,
    string ModelKey,
    string ModelName,
    DateTimeOffset StartedAt)
{
    public string DisplayText => $"{ModelName} · PID {ProcessId}";
}

internal sealed record ArkPetProcessHandle(
    Guid Id,
    Process Process,
    string ModelKey,
    string ModelName,
    DateTimeOffset StartedAt);

internal sealed class ArkPetsController : IAsyncDisposable
{
    private readonly IExtensionLogger logger;
    private readonly ArkPetsSettingsStore store = new();
    private readonly List<ArkPetProcessHandle> processes = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ArkPetsIpcServer ipcServer = new();
    private readonly ArkPetsUpstreamManager upstreamManager;
    private ArkPetsProcessJob? processJob;
    private ClassIslandIntegrationBridge? classIslandBridge;

    public ArkPetsController(IExtensionLogger logger)
    {
        this.logger = logger;
        upstreamManager = new ArkPetsUpstreamManager(() => Settings.NetworkProxy);
        ipcServer.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public ArkPetsSettings Settings { get; private set; } = new();
    public ArkModelsCatalog Catalog { get; private set; } = ArkModelsCatalog.Empty();
    public IReadOnlyList<ArkPetProcessSnapshot> RunningInstances
    {
        get
        {
            lock (processes)
            {
                return processes
                    .Where(handle => !handle.Process.HasExited)
                    .Select(handle => new ArkPetProcessSnapshot(
                        handle.Id,
                        handle.Process.Id,
                        handle.ModelKey,
                        handle.ModelName,
                        handle.StartedAt))
                    .OrderBy(handle => handle.StartedAt)
                    .ToArray();
            }
        }
    }

    public IReadOnlyList<ArkPetsIpcClientSnapshot> ControlledInstances => ipcServer.Clients;
    public int? ControlPort => ipcServer.IsRunning ? ipcServer.Port : null;
    public bool WindowsStartupEnabled => ExusiAIStartupBinding.IsEnabled();

    public bool ClassIslandAvailable =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExusiAI", "packages", "exusiai.misha-showcase", "package.json")) ||
        File.Exists(ClassIslandStateFile.DefaultPath);

    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Settings = await store.LoadAsync(cancellationToken);
        Settings = ApplyAutoDetection(Settings) with
        {
            StartExusiAIWithWindows = ExusiAIStartupBinding.IsEnabled(),
            LauncherSolidExit = true
        };
        if (!string.IsNullOrWhiteSpace(Settings.ModelRoot) && File.Exists(Path.Combine(Settings.ModelRoot, "models_data.json")))
            await ReloadModelsAsync(cancellationToken);
        else
            Changed?.Invoke(this, EventArgs.Empty);
        await store.SaveAsync(Settings, cancellationToken);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await ipcServer.StartAsync(cancellationToken);
            logger.Information($"ArkPets IPC host started on localhost:{ipcServer.Port}.");
        }
        catch (InvalidOperationException exception)
        {
            logger.Warning($"ArkPets IPC host unavailable: {exception.Message}");
        }

        classIslandBridge ??= new ClassIslandIntegrationBridge(this, logger);
        classIslandBridge.Start();

        if (Settings.AutoStartPetWithExusiAI &&
            SelectedModel is { IsAvailable: true } &&
            !string.IsNullOrWhiteSpace(Settings.RuntimePath) &&
            File.Exists(Settings.RuntimePath))
        {
            try
            {
                await LaunchSelectedAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.Warning($"ArkPets auto-start skipped: {exception.Message}");
            }
        }
    }

    public async Task UpdateSettingsAsync(
        Func<ArkPetsSettings, ArkPetsSettings> update,
        bool reloadModels = false,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Settings = update(Settings) with { LauncherSolidExit = true };
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

    public async Task<bool> SetWindowsStartupEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ExusiAIStartupBinding.SetEnabled(enabled))
            return false;

        await UpdateSettingsAsync(
            settings => settings with { StartExusiAIWithWindows = enabled },
            cancellationToken: cancellationToken);
        return true;
    }

    public async Task<string> InstallLatestRuntimeAsync(
        IProgress<ArkPetsDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var installed = await upstreamManager.InstallLatestRuntimeAsync(progress, cancellationToken);
        await UpdateSettingsAsync(
            settings => settings with
            {
                RuntimePath = installed.RuntimePath,
                RuntimeVersion = installed.Version
            },
            cancellationToken: cancellationToken);
        return installed.Version;
    }

    public async Task<int> InstallLatestModelsAsync(
        IProgress<ArkPetsDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = await upstreamManager.InstallLatestModelsAsync(progress, cancellationToken);
        await UpdateSettingsAsync(
            settings => settings with { ModelRoot = root, SelectedModelKey = "" },
            reloadModels: true,
            cancellationToken);
        return Catalog.Models.Count;
    }

    public Task<ArkPetsRuntimeRelease> QueryLatestRuntimeAsync(CancellationToken cancellationToken = default) =>
        upstreamManager.QueryLatestRuntimeAsync(cancellationToken);

    public Task<bool> SendControlAsync(
        Guid remoteId,
        ArkPetsIpcOperation operation,
        CancellationToken cancellationToken = default) =>
        ipcServer.SendAsync(remoteId, operation, cancellationToken);

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

    public bool IsFavorite(string modelKey) =>
        Settings.FavoriteModelKeys.Any(key => string.Equals(key, modelKey, StringComparison.OrdinalIgnoreCase));

    public async Task ToggleFavoriteAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        if (Catalog.Models.All(model => !string.Equals(model.Key, modelKey, StringComparison.OrdinalIgnoreCase)))
            return;

        var favorites = Settings.FavoriteModelKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!favorites.Add(modelKey))
            favorites.Remove(modelKey);

        await UpdateSettingsAsync(
            settings => settings with { FavoriteModelKeys = favorites.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray() },
            cancellationToken: cancellationToken);
    }

    public ArkModelsVerificationResult VerifyModelLibrary() => ArkModelsLibraryManager.Verify(Catalog);

    public Task ExportModelLibraryAsync(string destination, CancellationToken cancellationToken = default) =>
        ArkModelsLibraryManager.ExportAsync(Catalog, destination, cancellationToken);

    public async Task<string> ImportModelLibraryAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        using (var archive = System.IO.Compression.ZipFile.OpenRead(archivePath))
        {
            if (!archive.Entries.Any(entry =>
                    string.Equals(entry.Name, "models_data.json", StringComparison.OrdinalIgnoreCase)))
            {
                var model = await ArkModelsLibraryManager.ImportSingleModelAsync(Catalog, archivePath, cancellationToken);
                await ReloadModelsAsync(cancellationToken);
                await SelectModelAsync(model.Key, cancellationToken);
                return $"已导入单模型：{model.DisplayName}";
            }
        }

        var destinationRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExusiAI", "arkpets", "libraries");
        var root = await ArkModelsLibraryManager.ImportAsync(archivePath, destinationRoot, cancellationToken);
        await UpdateSettingsAsync(
            settings => settings with { ModelRoot = root, SelectedModelKey = "" },
            reloadModels: true,
            cancellationToken);
        return $"已导入完整模型库：{Catalog.Models.Count} 个模型";
    }

    public async Task<Process> LaunchSelectedAsync(CancellationToken cancellationToken = default)
    {
        var model = SelectedModel ?? throw new InvalidOperationException("请先选择一个桌宠模型。");
        if (!model.IsAvailable)
            throw new FileNotFoundException("所选模型文件不完整，请重新导入或更新 Ark-Models 模型库。");
        if (string.IsNullOrWhiteSpace(Settings.RuntimePath) || !File.Exists(Settings.RuntimePath))
            throw new FileNotFoundException("尚未设置可用的 ArkPets.exe 或 ArkPets.jar。", Settings.RuntimePath);
        if (string.IsNullOrWhiteSpace(Settings.ModelRoot) || !Directory.Exists(Settings.ModelRoot))
            throw new DirectoryNotFoundException("Ark-Models 模型库目录不可用。");

        if (!ipcServer.IsRunning)
            await ipcServer.StartAsync(cancellationToken);

        var configPath = await ArkPetsConfigWriter.WriteAsync(Settings, Catalog, model, cancellationToken);
        var info = BuildProcessStartInfo(Settings.RuntimePath, configPath);
        var process = Process.Start(info) ?? throw new InvalidOperationException("ArkPets 运行时未能启动。");
        try
        {
            processJob ??= new ArkPetsProcessJob();
            processJob.Assign(process);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            process.Dispose();
            throw;
        }

        var handle = new ArkPetProcessHandle(
            Guid.NewGuid(),
            process,
            model.Key,
            model.DisplayName,
            DateTimeOffset.Now);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            lock (processes)
            {
                processes.RemoveAll(item => item.Id == handle.Id);
            }
            process.Dispose();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        lock (processes)
        {
            processes.Add(handle);
        }
        logger.Information($"Started ArkPets model '{model.Key}'.");
        Changed?.Invoke(this, EventArgs.Empty);
        return process;
    }

    public async Task<bool> StopInstanceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ArkPetProcessHandle? handle;
        lock (processes)
            handle = processes.FirstOrDefault(item => item.Id == id);
        if (handle is null) return false;

        try
        {
            if (handle.Process.HasExited) return true;
            var requestedClose = handle.Process.CloseMainWindow();
            if (requestedClose)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    await handle.Process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Fall back to terminating only the process tree launched by this plugin.
                }
            }

            if (!handle.Process.HasExited)
                handle.Process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            logger.Warning($"Could not stop ArkPets process: {exception.Message}");
            return false;
        }
        finally
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        Guid[] ids;
        lock (processes)
            ids = processes.Select(handle => handle.Id).ToArray();
        foreach (var id in ids)
            await StopInstanceAsync(id, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        // Plugin mode is intentionally bound to ExusiAI: pets must not outlive the host.
        await StopAllAsync();
        processJob?.Dispose();
        processJob = null;
        await ipcServer.DisposeAsync();
        if (classIslandBridge is not null)
        {
            await classIslandBridge.DisposeAsync();
            classIslandBridge = null;
        }
        upstreamManager.Dispose();
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

    private static ProcessStartInfo BuildProcessStartInfo(string runtimePath, string configPath)
    {
        var isJar = string.Equals(Path.GetExtension(runtimePath), ".jar", StringComparison.OrdinalIgnoreCase);
        var runtimeDirectory = Path.GetDirectoryName(Path.GetFullPath(runtimePath))
            ?? throw new InvalidOperationException("ArkPets 运行时目录无效。");
        var info = new ProcessStartInfo
        {
            FileName = isJar ? "java" : runtimePath,
            WorkingDirectory = runtimeDirectory,
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
        var direct = candidates.FirstOrDefault(File.Exists);
        if (direct is not null)
            return direct;

        var managedRoot = Path.Combine(local, "ExusiAI", "arkpets", "runtime");
        return Directory.Exists(managedRoot)
            ? Directory.EnumerateFiles(managedRoot, "ArkPets.exe", SearchOption.AllDirectories)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .FirstOrDefault()
            : null;
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
        var direct = candidates.Where(x => !string.IsNullOrWhiteSpace(x))
            .FirstOrDefault(x => File.Exists(Path.Combine(x!, "models_data.json")));
        if (direct is not null)
            return direct;

        var managedLibraries = Path.Combine(local, "ExusiAI", "arkpets", "libraries");
        return Directory.Exists(managedLibraries)
            ? Directory.EnumerateFiles(managedLibraries, "models_data.json", SearchOption.AllDirectories)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
            : null;
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
        var assetPath = Path.GetFullPath(model.AssetDirectory).Replace('\\', '/');

        var files = new JsonObject();
        foreach (var pair in model.AssetFiles)
        {
            files[pair.Key] = pair.Value.Count == 1
                ? JsonValue.Create(pair.Value[0])
                : new JsonArray(pair.Value.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        }

        var favorites = new JsonObject();
        foreach (var key in settings.FavoriteModelKeys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.OrdinalIgnoreCase))
            favorites[key] = new JsonObject();

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
            ["canvas_color"] = settings.CanvasColor,
            ["canvas_coverage"] = settings.CanvasCoverage,
            ["canvas_sampling_interval"] = settings.CanvasSamplingInterval,
            ["character_asset"] = assetPath,
            ["character_favorites"] = favorites,
            ["character_files"] = files,
            ["character_label"] = model.Name,
            ["display_fps"] = settings.DisplayFps,
            ["display_margin_bottom"] = settings.DisplayMarginBottom,
            ["display_multi_monitors"] = settings.DisplayMultiMonitors,
            ["display_scale"] = settings.DisplayScale,
            ["download_mc_cdk"] = "",
            ["eco_mode"] = settings.EcoMode,
            ["enable_telemetry"] = false,
            ["initial_position_x"] = settings.InitialPositionX,
            ["initial_position_y"] = settings.InitialPositionY,
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
            ["render_outline_emphasis"] = settings.RenderOutlineEmphasis,
            ["render_outline_emphasis_color"] = settings.RenderOutlineEmphasisColor,
            ["render_outline_width"] = settings.RenderOutlineWidth,
            ["render_shader_high_quality"] = settings.RenderShaderHighQuality,
            ["render_shadow_color"] = settings.RenderShadowColor,
            ["transition_duration"] = settings.TransitionDuration,
            ["transition_type"] = settings.TransitionType,
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
