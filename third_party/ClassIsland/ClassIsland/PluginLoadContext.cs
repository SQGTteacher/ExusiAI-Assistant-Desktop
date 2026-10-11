using System.Reflection;
using System.Runtime.Loader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClassIsland.Core.Models.Plugin;
using ClassIsland.Services;

namespace ClassIsland;

/// <summary>
/// 为插件加载提供隔离的 <see cref="AssemblyLoadContext"/> 实现。<para/>
/// 根据运行平台选择不同的依赖解析器，并负责从插件目录解析托管与非托管依赖项。
/// <remarks>macOS平台的依赖解析器为<see cref="MacPluginAssemblyResolver"/></remarks>
/// </summary>
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly bool _suppressMacPluginLoader;
    private readonly string _componentAssemblyPath;
    private readonly HashSet<Assembly> _exportedApiAssemblies = new();
    private static readonly Lazy<HashSet<string>> RuntimeContracts = new(BuildRuntimeContracts);
    [ThreadStatic]
    private static HashSet<PluginLoadContext>? _readingExportedContracts;

    public PluginLoadContext(PluginInfo info, string fullPath, bool suppressMacPluginLoader) : base($"ClassIsland.PluginLoadContext[{info.Manifest.Id}]")
    {
        _suppressMacPluginLoader = suppressMacPluginLoader;
        _componentAssemblyPath = fullPath;
        Info = info;
        CoreResolver = UseMacOsPluginLoadingBehavior ? null : new(fullPath);
        MacResolver = UseMacOsPluginLoadingBehavior ? new(fullPath) : null;
    }

    /// <summary>
    /// 插件信息与清单引用，用于根据声明的依赖项查找并委托到其它插件的加载上下文。
    /// </summary>
    public PluginInfo Info { get; }

    private bool UseMacOsPluginLoadingBehavior =>
        _suppressMacPluginLoader || RuntimeInformation.IsOSPlatform(OSPlatform.OSX); 

    private AssemblyDependencyResolver? CoreResolver { get; }
    
    private MacPluginAssemblyResolver? MacResolver { get; }

    private static IReadOnlyList<string> WinRTDeps { get; } = [
        "WinRT.Runtime",
        "Microsoft.Windows.SDK.NET"
    ];

    /// <summary>
    /// 在需要加载程序集时被调用。优先从已加载的插件依赖项上下文中解析，如果在插件目录中找到对应的程序集则从路径加载。
    /// 对 WinRT 相关依赖会使用宿主的实现。
    /// </summary>
    protected override Assembly? Load(AssemblyName assemblyName) => LoadPluginAssembly(assemblyName, true);

    private Assembly? LoadPluginAssembly(AssemblyName assemblyName, bool allowRuntimeFallback)
    {
        if (WinRTDeps.Contains(assemblyName.Name))
        {
            // 为了防止因引用 WinRT 依赖导致重复初始化 WinRT 相关运行时使应用代码无法正常调用 WinRT，
            // 这里将插件要的加载的 WinRT 相关程序集替换为应用自带的 WinRT 相关程序集。
            return ResolveRuntimeAssembly(assemblyName);
        }
        // Embedded ClassIsland is not in AssemblyLoadContext.Default. SDK and
        // UI contracts must retain the runtime's identities, even if a plugin
        // carries its own copies, or its services/components cannot be assigned.
        if (IsRuntimeContract(assemblyName.Name) && ResolveRuntimeAssembly(assemblyName) is { } runtimeAssembly)
            return runtimeAssembly;
        // 尝试查找依赖
        foreach (var dep in Info.Manifest.Dependencies)
        {
            if (!PluginService.PluginLoadContexts.TryGetValue(dep.Id, out var context))
            {
                continue;
            }

            var assembly = context.LoadPluginAssembly(assemblyName, false);
            if (assembly != null)
            {
                return assembly;
            }
        }
        
        string? assemblyPath;
        assemblyPath = UseMacOsPluginLoadingBehavior ? MacResolver?.ResolveAssemblyToPath(assemblyName) : CoreResolver?.ResolveAssemblyToPath(assemblyName);
        
        if (assemblyPath != null)
        {
            var supplied = LoadFromAssemblyPath(assemblyPath);
            // A dependency consumer explicitly requesting a secondary assembly
            // makes its public API a boundary; internal implementation loads do not.
            if (!allowRuntimeFallback)
                lock (_exportedApiAssemblies) _exportedApiAssemblies.Add(supplied);
            return supplied;
        }

        // Dependency plugins may expose a runtime-provided library in their
        // public API. Only that contract may fall back during dependency probing.
        return allowRuntimeFallback || ExportsContract(assemblyName.Name)
            ? ResolveRuntimeAssembly(assemblyName) : null;
    }

    private bool ExportsContract(string? name)
    {
        if (name is null || GetLoadContext(typeof(PluginLoadContext).Assembly) == Default) return false;
        var reading = _readingExportedContracts ??= new HashSet<PluginLoadContext>();
        if (!reading.Add(this)) return false;
        try
        {
            // Seed only the plugin entry API, not every private implementation in
            // its package. Public signatures reach secondary APIs even before
            // consumers request them, regardless of their assembly-name prefix.
            var entry = LoadFromAssemblyPath(_componentAssemblyPath);
            Assembly[] roots;
            lock (_exportedApiAssemblies) roots = _exportedApiAssemblies.ToArray();
            return ReadContracts(new[] { entry }.Concat(roots).Concat(Assemblies.Where(assembly => assembly.IsDynamic)).ToArray()).Contains(name);
        }
        finally { reading.Remove(this); }
    }

    private static bool IsRuntimeContract(string? name) => name is not null &&
        GetLoadContext(typeof(PluginLoadContext).Assembly) != Default &&
        (name == "ClassIsland" || name.StartsWith("ClassIsland.", StringComparison.Ordinal) ||
         name == "Avalonia" || name.StartsWith("Avalonia.", StringComparison.Ordinal) ||
         RuntimeContracts.Value.Contains(name));

    private static HashSet<string> BuildRuntimeContracts()
    {
        // Derive the boundary from the real SDK, including public bases, generic
        // arguments and method signatures. A hand-maintained list misses APIs
        // such as MarkdownConvertHelper.Engine and IAudioService.AudioEngine.
        return ReadContracts(new[] { typeof(PluginInfo).Assembly, typeof(ClassIsland.Shared.IAppHost).Assembly });
    }

    private static HashSet<string> ReadContracts(IEnumerable<Assembly> assemblies)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<Type>();
        var pending = new Queue<Type>();
        void AddArgument(CustomAttributeTypedArgument argument)
        {
            AddType(argument.ArgumentType);
            if (argument.Value is Type type) AddType(type);
            if (argument.Value is IEnumerable<CustomAttributeTypedArgument> items)
                foreach (var item in items) AddArgument(item);
        }
        void AddAttributes(IEnumerable<CustomAttributeData> attributes)
        {
            foreach (var attribute in attributes)
            {
                AddType(attribute.AttributeType);
                foreach (var argument in attribute.ConstructorArguments) AddArgument(argument);
                foreach (var argument in attribute.NamedArguments) AddArgument(argument.TypedValue);
            }
        }
        void AddType(Type? type)
        {
            if (type is null || !visited.Add(type)) return;
            if (type.IsGenericParameter)
            {
                AddAttributes(type.CustomAttributes);
                foreach (var constraint in type.GetGenericParameterConstraints()) AddType(constraint);
                return;
            }
            var name = type.Assembly.GetName().Name;
            if (name is not null) names.Add(name);
            if (type.HasElementType) AddType(type.GetElementType());
            if (type.IsGenericType)
                foreach (var argument in type.GetGenericArguments()) AddType(argument);
            // Framework types already share the CLR's identity. Traversing their
            // entire API would turn object/Type into an unrelated assembly sweep.
            if (!type.HasElementType && name is not null && name != "mscorlib" && name != "netstandard" &&
                name != "System" && !name.StartsWith("System.", StringComparison.Ordinal) &&
                GetLoadContext(type.Assembly) != Default)
                pending.Enqueue(type);
        }
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var sdk in assemblies)
        foreach (var type in sdk.IsDynamic ? sdk.GetTypes().Where(type => type.IsVisible)
                     : sdk.GetExportedTypes().Concat(sdk.GetForwardedTypes()))
            AddType(type);
        // Follow public bases and exposed external types as well as SDK declarations.
        // This includes HostBuilderContext.Configuration and inherited plugin APIs,
        // without sharing unrelated Microsoft.Extensions implementation assemblies.
        while (pending.Count != 0)
        {
            var type = pending.Dequeue();
            AddAttributes(type.CustomAttributes);
            AddType(type.BaseType);
            foreach (var contract in type.GetInterfaces()) AddType(contract);
            foreach (var field in type.GetFields(flags)) { AddType(field.FieldType); AddAttributes(field.CustomAttributes); }
            foreach (var property in type.GetProperties(flags)) { AddType(property.PropertyType); AddAttributes(property.CustomAttributes); }
            foreach (var @event in type.GetEvents(flags)) { AddType(@event.EventHandlerType); AddAttributes(@event.CustomAttributes); }
            foreach (var method in type.GetMethods(flags))
            {
                AddType(method.ReturnType);
                AddAttributes(method.CustomAttributes);
                AddAttributes(method.ReturnParameter.CustomAttributes);
                foreach (var parameter in method.GetParameters()) { AddType(parameter.ParameterType); AddAttributes(parameter.CustomAttributes); }
                foreach (var argument in method.GetGenericArguments()) AddType(argument);
            }
            foreach (var constructor in type.GetConstructors(flags))
            {
                AddAttributes(constructor.CustomAttributes);
                foreach (var parameter in constructor.GetParameters()) { AddType(parameter.ParameterType); AddAttributes(parameter.CustomAttributes); }
            }
        }
        return names;
    }

    private static Assembly? ResolveRuntimeAssembly(AssemblyName name)
    {
        var runtime = GetLoadContext(typeof(PluginLoadContext).Assembly);
        if (runtime is null || runtime == Default) return null;
        try { return runtime.LoadFromAssemblyName(name); }
        catch (FileNotFoundException) { return null; }
    }

    /// <summary>
    /// 解析并加载插件的非托管（本地）库，按平台和插件目录的约定搜索文件。
    /// 返回非托管库句柄，找不到则返回 <see cref="IntPtr.Zero"/>。
    /// </summary>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath;
        libraryPath = UseMacOsPluginLoadingBehavior ? MacResolver?.ResolveUnmanagedDllToPath(unmanagedDllName) : CoreResolver?.ResolveUnmanagedDllToPath(unmanagedDllName);

        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}

/// <summary>
/// macOS 专用的插件程序集与本地库解析器。根据插件目录结构查找真实文件路径。
/// </summary>
[SupportedOSPlatform("macos")]
public class MacPluginAssemblyResolver(string componentAssemblyPath)
{
    private readonly string _pluginDirectory = Path.GetDirectoryName(componentAssemblyPath) ?? "";

    /// <summary>
    /// 将程序集名解析为插件目录下的 dll 文件路径（若存在），否则返回 null。
    /// </summary>
    public string? ResolveAssemblyToPath(AssemblyName assemblyName)
    {
        var dllPath = Path.Combine(_pluginDirectory, assemblyName.Name + ".dll");
        if (File.Exists(dllPath))
            return dllPath;
        return null;
    }

    /// <summary>
    /// 在插件目录及其可能的 runtimes 子目录中查找非托管库文件，并返回第一个匹配的完整路径。
    /// 支持按处理器架构查找文件。
    /// </summary>
    public string? ResolveUnmanagedDllToPath(string unmanagedDllName)
    {
        var searchPaths = new List<string>
        {
            _pluginDirectory,
            Path.Combine(_pluginDirectory, "runtimes", "osx", "native")
        };
        
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };
        
        if (arch != null)
        {
            searchPaths.Add(Path.Combine(_pluginDirectory, "runtimes", $"osx-{arch}", "native"));
        }

        foreach (var path in searchPaths)
        {
            if (!Directory.Exists(path)) continue;
            
            var p1 = Path.Combine(path, $"lib{unmanagedDllName}.dylib");
            if (File.Exists(p1)) return p1;
            
            var p2 = Path.Combine(path, $"{unmanagedDllName}.dylib");
            if (File.Exists(p2)) return p2;

            var p3 = Path.Combine(path, unmanagedDllName);
            if (File.Exists(p3)) return p3;
        }

        return null;
    }
}
