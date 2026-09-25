using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ExusiAI.Plugin.MishaShowcase;

internal enum ClassIslandThemeVariant
{
    Default,
    Light,
    Dark
}

internal readonly record struct ClassIslandThemeColor(byte A, byte R, byte G, byte B)
{
    public static bool TryParseAxaml(string? text, out ClassIslandThemeColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        if (!value.StartsWith('#'))
        {
            switch (value.ToLowerInvariant())
            {
                case "transparent":
                    color = new(0, 0, 0, 0);
                    return true;
                case "black":
                    color = new(255, 0, 0, 0);
                    return true;
                case "white":
                    color = new(255, 255, 255, 255);
                    return true;
                case "red":
                    color = new(255, 255, 0, 0);
                    return true;
                case "dodgerblue":
                    color = new(255, 30, 144, 255);
                    return true;
                default:
                    return false;
            }
        }

        var hex = value[1..];
        if (hex.Length == 8 &&
            TryByte(hex.AsSpan(0, 2), out var a) &&
            TryByte(hex.AsSpan(2, 2), out var r) &&
            TryByte(hex.AsSpan(4, 2), out var g) &&
            TryByte(hex.AsSpan(6, 2), out var b))
        {
            color = new(a, r, g, b);
            return true;
        }

        if (hex.Length == 6 &&
            TryByte(hex.AsSpan(0, 2), out r) &&
            TryByte(hex.AsSpan(2, 2), out g) &&
            TryByte(hex.AsSpan(4, 2), out b))
        {
            color = new(255, r, g, b);
            return true;
        }

        if (hex.Length == 4 &&
            TryNibble(hex[0], out var an) &&
            TryNibble(hex[1], out var rn) &&
            TryNibble(hex[2], out var gn) &&
            TryNibble(hex[3], out var bn))
        {
            color = new(Expand(an), Expand(rn), Expand(gn), Expand(bn));
            return true;
        }

        if (hex.Length == 3 &&
            TryNibble(hex[0], out rn) &&
            TryNibble(hex[1], out gn) &&
            TryNibble(hex[2], out bn))
        {
            color = new(255, Expand(rn), Expand(gn), Expand(bn));
            return true;
        }

        return false;
    }

    private static bool TryByte(ReadOnlySpan<char> value, out byte result) =>
        byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);

    private static bool TryNibble(char value, out byte result)
    {
        if (value is >= '0' and <= '9')
        {
            result = (byte)(value - '0');
            return true;
        }
        if (value is >= 'a' and <= 'f')
        {
            result = (byte)(value - 'a' + 10);
            return true;
        }
        if (value is >= 'A' and <= 'F')
        {
            result = (byte)(value - 'A' + 10);
            return true;
        }
        result = 0;
        return false;
    }

    private static byte Expand(byte value) => (byte)((value << 4) | value);
}

internal readonly record struct ClassIslandThemePoint(double X, double Y);

internal readonly record struct ClassIslandThemeGradientStop(double Offset, ClassIslandThemeColor Color);

internal abstract record ClassIslandThemeResource(string Kind);

internal sealed record ClassIslandThemeColorResource(ClassIslandThemeColor Color)
    : ClassIslandThemeResource("Color");

internal sealed record ClassIslandThemeSolidBrushResource(ClassIslandThemeColor Color, double Opacity = 1)
    : ClassIslandThemeResource("SolidColorBrush");

internal sealed record ClassIslandThemeLinearGradientResource(
    ClassIslandThemePoint StartPoint,
    ClassIslandThemePoint EndPoint,
    IReadOnlyList<ClassIslandThemeGradientStop> Stops)
    : ClassIslandThemeResource("LinearGradientBrush");

internal sealed record ClassIslandThemeRadialGradientResource(
    ClassIslandThemePoint Center,
    ClassIslandThemePoint GradientOrigin,
    double RadiusX,
    double RadiusY,
    IReadOnlyList<ClassIslandThemeGradientStop> Stops)
    : ClassIslandThemeResource("RadialGradientBrush");

internal sealed record ClassIslandThemeConicGradientResource(
    ClassIslandThemePoint Center,
    double Angle,
    IReadOnlyList<ClassIslandThemeGradientStop> Stops)
    : ClassIslandThemeResource("ConicGradientBrush");

internal sealed record ClassIslandThemeDrawingLayer(
    string GeometryKind,
    string GeometryData,
    ClassIslandThemeResource Brush);

internal sealed record ClassIslandThemeDrawingBrushResource(
    IReadOnlyList<ClassIslandThemeDrawingLayer> Layers)
    : ClassIslandThemeResource("DrawingBrush");

internal sealed record ClassIslandThemeLiteralResource(string TypeName, string RawValue)
    : ClassIslandThemeResource(TypeName);

internal sealed record ClassIslandThemeValue(
    string? Literal,
    string? ResourceKey,
    bool DynamicResource,
    ClassIslandThemeResource? InlineResource)
{
    private static readonly Regex ResourceRegex = new(
        @"^\{(?<kind>DynamicResource|StaticResource)\s+(?<key>[^\}]+)\}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static ClassIslandThemeValue Parse(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        var match = ResourceRegex.Match(value);
        if (match.Success)
        {
            return new(
                null,
                match.Groups["key"].Value.Trim(),
                match.Groups["kind"].Value.Equals("DynamicResource", StringComparison.OrdinalIgnoreCase),
                null);
        }

        return new(value, null, false, null);
    }

    public static ClassIslandThemeValue FromInline(ClassIslandThemeResource resource) =>
        new(null, null, false, resource);
}

internal sealed record ClassIslandThemeSelectorCondition(string Property, string ExpectedValue);

internal sealed class ClassIslandThemeSelector
{
    private static readonly Regex TypeRegex = new(
        @"^\s*(?<type>[A-Za-z_][A-Za-z0-9_\-\|:]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ClassRegex = new(
        @"\.(?<class>[A-Za-z_][A-Za-z0-9_\-]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NameRegex = new(
        @"#(?<name>[A-Za-z_][A-Za-z0-9_\-]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AttachedConditionRegex = new(
        @"\[\((?<property>[^\)]+)\)=(?<value>[^\]]+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ConditionRegex = new(
        @"\[(?<property>[A-Za-z_][A-Za-z0-9_\-\.\|:]*)=(?<value>[^\]]+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private ClassIslandThemeSelector(
        string raw,
        string? typeName,
        string? name,
        IReadOnlySet<string> classes,
        IReadOnlyList<ClassIslandThemeSelectorCondition> conditions,
        bool supported)
    {
        Raw = raw;
        TypeName = typeName;
        Name = name;
        Classes = classes;
        Conditions = conditions;
        IsSupported = supported;
    }

    public string Raw { get; }
    public string? TypeName { get; }
    public string? Name { get; }
    public IReadOnlySet<string> Classes { get; }
    public IReadOnlyList<ClassIslandThemeSelectorCondition> Conditions { get; }
    public bool IsSupported { get; }

    public static ClassIslandThemeSelector Parse(string raw)
    {
        var conditions = new List<ClassIslandThemeSelectorCondition>();
        foreach (Match match in AttachedConditionRegex.Matches(raw))
        {
            conditions.Add(new(
                NormalizeProperty(match.Groups["property"].Value),
                match.Groups["value"].Value.Trim().Trim('"', '\'')));
        }

        foreach (Match match in ConditionRegex.Matches(raw))
        {
            var property = NormalizeProperty(match.Groups["property"].Value);
            if (conditions.Any(x => x.Property.Equals(property, StringComparison.OrdinalIgnoreCase)))
                continue;
            conditions.Add(new(property, match.Groups["value"].Value.Trim().Trim('"', '\'')));
        }

        var simpleSelector = AttachedConditionRegex.Replace(raw, string.Empty);
        simpleSelector = ConditionRegex.Replace(simpleSelector, string.Empty);

        var typeMatch = TypeRegex.Match(simpleSelector);
        var type = typeMatch.Success ? NormalizeType(typeMatch.Groups["type"].Value) : null;
        var nameMatch = NameRegex.Match(simpleSelector);
        var name = nameMatch.Success ? nameMatch.Groups["name"].Value : null;
        var classes = ClassRegex.Matches(simpleSelector)
            .Select(x => x.Groups["class"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stripped = ClassRegex.Replace(simpleSelector, string.Empty);
        stripped = NameRegex.Replace(stripped, string.Empty);
        if (typeMatch.Success)
            stripped = stripped[typeMatch.Length..];

        var unsupported = stripped.Contains(':') ||
                          stripped.Contains('>') ||
                          stripped.Contains('+') ||
                          stripped.Contains('~') ||
                          stripped.Trim().Contains(' ');

        return new(raw, type, name, classes, conditions, !unsupported);
    }

    public bool Matches(ClassIslandThemeTarget target)
    {
        if (!IsSupported)
            return false;
        if (!string.IsNullOrWhiteSpace(TypeName) &&
            !TypeName.Equals(target.TypeName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(Name) &&
            !Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase))
            return false;
        if (Classes.Any(required => !target.Classes.Contains(required)))
            return false;

        foreach (var condition in Conditions)
        {
            if (!target.Properties.TryGetValue(condition.Property, out var actual) ||
                !actual.Equals(condition.ExpectedValue, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string NormalizeType(string value)
    {
        var pipe = value.LastIndexOf('|');
        return pipe >= 0 ? value[(pipe + 1)..] : value;
    }

    private static string NormalizeProperty(string value)
    {
        var trimmed = value.Trim();
        var pipe = trimmed.LastIndexOf('|');
        return pipe >= 0 ? trimmed[(pipe + 1)..] : trimmed;
    }
}

internal sealed record ClassIslandThemeStyle(
    string SelectorText,
    ClassIslandThemeSelector Selector,
    IReadOnlyDictionary<string, ClassIslandThemeValue> Setters,
    IReadOnlyDictionary<string, ClassIslandThemeResource> Resources);

internal sealed class ClassIslandThemeTarget
{
    public ClassIslandThemeTarget(
        string typeName,
        IEnumerable<string>? classes = null,
        IReadOnlyDictionary<string, string>? properties = null,
        string? name = null)
    {
        TypeName = typeName;
        Name = name;
        Classes = (classes ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Properties = properties is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : properties.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    }

    public string TypeName { get; }
    public string? Name { get; }
    public IReadOnlySet<string> Classes { get; }
    public IReadOnlyDictionary<string, string> Properties { get; }
}

internal sealed record ClassIslandThemeManifest(
    string Id,
    string Name,
    string Author,
    string Version,
    string Description,
    string Banner,
    double VerticalSafeAreaPx);

internal sealed class ClassIslandThemeDocument
{
    private readonly Dictionary<ClassIslandThemeVariant, Dictionary<string, ClassIslandThemeResource>> resources = new()
    {
        [ClassIslandThemeVariant.Default] = new(StringComparer.OrdinalIgnoreCase),
        [ClassIslandThemeVariant.Light] = new(StringComparer.OrdinalIgnoreCase),
        [ClassIslandThemeVariant.Dark] = new(StringComparer.OrdinalIgnoreCase)
    };

    public IReadOnlyDictionary<ClassIslandThemeVariant, Dictionary<string, ClassIslandThemeResource>> Resources => resources;
    public List<ClassIslandThemeStyle> Styles { get; } = [];
    public List<string> Diagnostics { get; } = [];

    public Dictionary<string, ClassIslandThemeResource> ForVariant(ClassIslandThemeVariant variant) => resources[variant];

    public void MergeFrom(ClassIslandThemeDocument other)
    {
        foreach (var variant in resources.Keys)
        {
            foreach (var pair in other.resources[variant])
                resources[variant][pair.Key] = pair.Value;
        }
        Styles.AddRange(other.Styles);
        Diagnostics.AddRange(other.Diagnostics);
    }
}

internal sealed record ClassIslandThemePackage(
    ClassIslandThemeManifest Manifest,
    string SourcePath,
    bool IsArchive,
    bool Enabled,
    ClassIslandThemeDocument? Document,
    IReadOnlyList<string> Diagnostics)
{
    public bool Parsed => Document is not null;
}

internal sealed record ClassIslandResolvedThemeSetter(
    ClassIslandThemeValue Value,
    IReadOnlyDictionary<string, ClassIslandThemeResource> LocalResources);

internal sealed class ClassIslandThemeSnapshot
{
    public static ClassIslandThemeSnapshot Empty { get; } = new(
        [],
        [],
        [],
        0);

    public ClassIslandThemeSnapshot(
        IReadOnlyList<string> enabledThemeIds,
        IReadOnlyList<ClassIslandThemePackage> packages,
        IReadOnlyList<string> diagnostics,
        double actualVerticalSafeAreaPx)
    {
        EnabledThemeIds = enabledThemeIds;
        Packages = packages;
        Diagnostics = diagnostics;
        ActualVerticalSafeAreaPx = actualVerticalSafeAreaPx;
    }

    public IReadOnlyList<string> EnabledThemeIds { get; }
    public IReadOnlyList<ClassIslandThemePackage> Packages { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    public double ActualVerticalSafeAreaPx { get; }

    public ClassIslandThemeVariant ResolveVariant(int classIslandTheme, bool systemLight)
    {
        return classIslandTheme switch
        {
            1 => ClassIslandThemeVariant.Light,
            2 => ClassIslandThemeVariant.Dark,
            _ => systemLight ? ClassIslandThemeVariant.Light : ClassIslandThemeVariant.Dark
        };
    }

    public ClassIslandThemeResource? ResolveResource(string key, ClassIslandThemeVariant variant)
    {
        ClassIslandThemeResource? resolved = null;
        foreach (var id in EnabledThemeIds)
        {
            var package = Packages.LastOrDefault(x =>
                x.Enabled &&
                x.Manifest.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            var document = package?.Document;
            if (document is null)
                continue;

            if (document.ForVariant(ClassIslandThemeVariant.Default).TryGetValue(key, out var common))
                resolved = common;
            if (variant != ClassIslandThemeVariant.Default &&
                document.ForVariant(variant).TryGetValue(key, out var themed))
                resolved = themed;
        }
        return resolved;
    }

    public IReadOnlyDictionary<string, ClassIslandResolvedThemeSetter> ResolveSetters(ClassIslandThemeTarget target)
    {
        var result = new Dictionary<string, ClassIslandResolvedThemeSetter>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in EnabledThemeIds)
        {
            var package = Packages.LastOrDefault(x =>
                x.Enabled &&
                x.Manifest.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            var document = package?.Document;
            if (document is null)
                continue;

            foreach (var style in document.Styles)
            {
                if (!style.Selector.Matches(target))
                    continue;
                foreach (var setter in style.Setters)
                    result[setter.Key] = new(setter.Value, style.Resources);
            }
        }
        return result;
    }
}

internal static class ClassIslandThemeCompatibilityLayer
{
    private const int MaximumThemeTextBytes = 4 * 1024 * 1024;
    private const int MaximumIncludeDepth = 8;

    public static ClassIslandThemeSnapshot Load(ClassIslandWorkspace workspace)
    {
        var enabled = LoadEnabledThemes(workspace.EnabledThemesPath);
        var discovered = new List<ClassIslandThemePackage>();
        var diagnostics = new List<string>();

        if (Directory.Exists(workspace.ThemesDirectory))
        {
            foreach (var directory in Directory.EnumerateDirectories(workspace.ThemesDirectory))
            {
                var package = LoadDirectoryPackage(directory, enabled);
                discovered.Add(package);
                diagnostics.AddRange(package.Diagnostics.Select(x => $"{package.Manifest.Id}: {x}"));
            }

            foreach (var archive in Directory.EnumerateFiles(workspace.ThemesDirectory, "*.zip", SearchOption.TopDirectoryOnly))
            {
                var package = LoadArchivePackage(archive, enabled);
                if (discovered.Any(x => x.Manifest.Id.Equals(package.Manifest.Id, StringComparison.OrdinalIgnoreCase) && !x.IsArchive))
                    continue;
                discovered.Add(package);
                diagnostics.AddRange(package.Diagnostics.Select(x => $"{package.Manifest.Id}: {x}"));
            }
        }

        foreach (var id in enabled)
        {
            if (discovered.Any(x => x.Manifest.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                continue;

            discovered.Add(new(
                CreateIntegratedManifest(id),
                "integrated",
                false,
                true,
                null,
                ["未发现本地安全可解析的 AXAML；保留为 ClassIsland 集成主题基线。"]));
        }

        var safeArea = discovered
            .Where(x => x.Enabled)
            .Select(x => x.Manifest.VerticalSafeAreaPx)
            .DefaultIfEmpty(0)
            .Max();

        return new(
            enabled,
            discovered,
            diagnostics,
            safeArea);
    }

    public static IReadOnlyList<string> LoadEnabledThemes(string path)
    {
        try
        {
            if (!File.Exists(path))
                return ["classisland.fluent"];

            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonArray;
            if (root is null)
                return ["classisland.fluent"];

            var result = root
                .OfType<JsonValue>()
                .Select(x => x.TryGetValue<string>(out var value) ? value : null)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (result.Count == 0)
                result.Add("classisland.fluent");
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return ["classisland.fluent"];
        }
    }

    private static ClassIslandThemePackage LoadDirectoryPackage(string directory, IReadOnlyList<string> enabled)
    {
        var diagnostics = new List<string>();
        var manifestPath = Path.Combine(directory, "manifest.yml");
        var manifest = File.Exists(manifestPath)
            ? ParseManifest(SafeReadText(manifestPath), Path.GetFileName(directory))
            : new(
                Path.GetFileName(directory),
                Path.GetFileName(directory),
                string.Empty,
                string.Empty,
                string.Empty,
                "banner.png",
                0);

        ClassIslandThemeDocument? document = null;
        var stylesPath = Path.Combine(directory, "Styles.axaml");
        if (File.Exists(stylesPath))
        {
            try
            {
                document = ParseAxaml(
                    SafeReadText(stylesPath),
                    relativePath => SafeReadRelativeDirectoryFile(directory, relativePath),
                    "Styles.axaml",
                    diagnostics);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or XmlException)
            {
                diagnostics.Add($"Styles.axaml 解析失败：{exception.Message}");
            }
        }
        else
        {
            diagnostics.Add("未找到 Styles.axaml。");
        }

        return new(
            manifest,
            Path.GetFullPath(directory),
            false,
            enabled.Contains(manifest.Id, StringComparer.OrdinalIgnoreCase),
            document,
            diagnostics);
    }

    private static ClassIslandThemePackage LoadArchivePackage(string archivePath, IReadOnlyList<string> enabled)
    {
        var diagnostics = new List<string>();
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var manifestEntry = FindEntry(archive, "manifest.yml");
            var fallbackId = Path.GetFileNameWithoutExtension(archivePath);
            var manifest = manifestEntry is null
                ? new(fallbackId, fallbackId, string.Empty, string.Empty, string.Empty, "banner.png", 0)
                : ParseManifest(SafeReadEntry(manifestEntry), fallbackId);

            var stylesEntry = FindEntry(archive, "Styles.axaml");
            ClassIslandThemeDocument? document = null;
            if (stylesEntry is not null)
            {
                document = ParseAxaml(
                    SafeReadEntry(stylesEntry),
                    relativePath => SafeReadRelativeArchiveEntry(archive, relativePath),
                    "Styles.axaml",
                    diagnostics);
            }
            else
            {
                diagnostics.Add("主题包中未找到 Styles.axaml。");
            }

            return new(
                manifest,
                Path.GetFullPath(archivePath),
                true,
                enabled.Contains(manifest.Id, StringComparer.OrdinalIgnoreCase),
                document,
                diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var fallbackId = Path.GetFileNameWithoutExtension(archivePath);
            diagnostics.Add($"主题包解析失败：{exception.Message}");
            return new(
                new(fallbackId, fallbackId, string.Empty, string.Empty, string.Empty, string.Empty, 0),
                Path.GetFullPath(archivePath),
                true,
                enabled.Contains(fallbackId, StringComparer.OrdinalIgnoreCase),
                null,
                diagnostics);
        }
    }

    private static ClassIslandThemeManifest CreateIntegratedManifest(string id)
    {
        return id.ToLowerInvariant() switch
        {
            "classisland.fluent" => new(
                "classisland.fluent",
                "Fluent",
                "ClassIsland",
                string.Empty,
                "ClassIsland 内置 Fluent 主界面主题。",
                string.Empty,
                20),
            "classisland.classic" => new(
                "classisland.classic",
                "经典",
                "ClassIsland",
                string.Empty,
                "ClassIsland 内置经典主界面主题。",
                string.Empty,
                0),
            _ => new(
                id,
                id,
                "ClassIsland / plugin",
                string.Empty,
                "由 ClassIsland 内置或插件注册的主题。",
                string.Empty,
                0)
        };
    }

    private static ClassIslandThemeManifest ParseManifest(string yaml, string fallbackId)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in yaml.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            values[key] = value;
        }

        var id = values.GetValueOrDefault("id");
        if (string.IsNullOrWhiteSpace(id))
            id = fallbackId;
        var name = values.GetValueOrDefault("name");
        if (string.IsNullOrWhiteSpace(name))
            name = id;

        _ = double.TryParse(
            values.GetValueOrDefault("verticalSafeAreaPx"),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var safeArea);

        return new(
            id,
            name,
            values.GetValueOrDefault("author") ?? string.Empty,
            values.GetValueOrDefault("version") ?? string.Empty,
            values.GetValueOrDefault("description") ?? string.Empty,
            values.GetValueOrDefault("banner") ?? "banner.png",
            Math.Max(0, safeArea));
    }

    private static ClassIslandThemeDocument ParseAxaml(
        string xml,
        Func<string, string?> readRelative,
        string currentPath,
        List<string> packageDiagnostics)
    {
        var result = new ClassIslandThemeDocument();
        ParseAxamlInto(result, xml, readRelative, currentPath, packageDiagnostics, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
        return result;
    }

    private static void ParseAxamlInto(
        ClassIslandThemeDocument result,
        string xml,
        Func<string, string?> readRelative,
        string currentPath,
        List<string> packageDiagnostics,
        HashSet<string> visited,
        int depth)
    {
        if (depth > MaximumIncludeDepth)
        {
            packageDiagnostics.Add($"样式包含深度超过 {MaximumIncludeDepth}：{currentPath}");
            return;
        }

        var normalizedCurrent = NormalizeRelativePath(currentPath);
        if (!visited.Add(normalizedCurrent))
            return;

        var document = LoadXml(xml);
        var root = document.Root ?? throw new InvalidDataException("AXAML 缺少根元素。");

        foreach (var include in root.Descendants().Where(x => x.Name.LocalName == "StyleInclude"))
        {
            var source = include.Attribute("Source")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(source))
                continue;
            if (source.StartsWith("avares://ClassIsland", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("/Controls/", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("/XamlThemes/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (Uri.TryCreate(source, UriKind.Absolute, out _))
            {
                packageDiagnostics.Add($"忽略外部 StyleInclude：{source}");
                continue;
            }

            var relative = ResolveRelativePath(normalizedCurrent, source);
            var included = readRelative(relative);
            if (included is null)
            {
                packageDiagnostics.Add($"找不到主题内 StyleInclude：{relative}");
                continue;
            }

            ParseAxamlInto(result, included, readRelative, relative, packageDiagnostics, visited, depth + 1);
        }

        foreach (var resourcesNode in root.Elements().Where(x => x.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)))
            ParseResourcesContainer(result, resourcesNode, packageDiagnostics);

        foreach (var style in root.Elements().Where(x => x.Name.LocalName == "Style"))
            ParseStyle(result, style, packageDiagnostics);
    }

    private static XDocument LoadXml(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = MaximumThemeTextBytes
        };
        using var textReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(textReader, settings);
        return XDocument.Load(xmlReader, LoadOptions.None);
    }

    private static void ParseResourcesContainer(
        ClassIslandThemeDocument document,
        XElement resourcesNode,
        List<string> diagnostics)
    {
        var dictionary = resourcesNode.Elements().FirstOrDefault(x => x.Name.LocalName == "ResourceDictionary");
        if (dictionary is null)
        {
            foreach (var resource in resourcesNode.Elements())
                AddResource(document.ForVariant(ClassIslandThemeVariant.Default), resource, diagnostics);
            return;
        }

        foreach (var child in dictionary.Elements())
        {
            if (child.Name.LocalName == "ResourceDictionary.ThemeDictionaries")
            {
                foreach (var themedDictionary in child.Elements().Where(x => x.Name.LocalName == "ResourceDictionary"))
                {
                    var variant = ParseVariant(ReadKey(themedDictionary));
                    foreach (var resource in themedDictionary.Elements())
                        AddResource(document.ForVariant(variant), resource, diagnostics);
                }
            }
            else
            {
                AddResource(document.ForVariant(ClassIslandThemeVariant.Default), child, diagnostics);
            }
        }
    }

    private static void ParseStyle(
        ClassIslandThemeDocument document,
        XElement style,
        List<string> diagnostics)
    {
        var selectorText = style.Attribute("Selector")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(selectorText))
            return;

        var selector = ClassIslandThemeSelector.Parse(selectorText);
        if (!selector.IsSupported)
            diagnostics.Add($"未执行复杂选择器，仅保留：{selectorText}");

        var setters = new Dictionary<string, ClassIslandThemeValue>(StringComparer.OrdinalIgnoreCase);
        var localResources = new Dictionary<string, ClassIslandThemeResource>(StringComparer.OrdinalIgnoreCase);

        foreach (var child in style.Elements())
        {
            if (child.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal))
            {
                var dictionary = child.Elements().FirstOrDefault(x => x.Name.LocalName == "ResourceDictionary");
                var nodes = dictionary?.Elements() ?? child.Elements();
                foreach (var resource in nodes)
                    AddResource(localResources, resource, diagnostics);
                continue;
            }

            if (child.Name.LocalName != "Setter")
                continue;

            var property = child.Attribute("Property")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(property))
                continue;

            var valueAttribute = child.Attribute("Value")?.Value;
            if (valueAttribute is not null)
            {
                setters[property] = ClassIslandThemeValue.Parse(valueAttribute);
                continue;
            }

            var valueContainer = child.Elements().FirstOrDefault(x => x.Name.LocalName.EndsWith(".Value", StringComparison.Ordinal));
            var inlineElement = valueContainer?.Elements().FirstOrDefault() ?? child.Elements().FirstOrDefault();
            if (inlineElement is not null && TryParseResource(inlineElement, diagnostics, out var inline))
                setters[property] = ClassIslandThemeValue.FromInline(inline);
        }

        document.Styles.Add(new(selectorText, selector, setters, localResources));
    }

    private static void AddResource(
        IDictionary<string, ClassIslandThemeResource> destination,
        XElement element,
        List<string> diagnostics)
    {
        var key = ReadKey(element);
        if (string.IsNullOrWhiteSpace(key))
            return;
        if (TryParseResource(element, diagnostics, out var resource))
            destination[key] = resource;
    }

    private static bool TryParseResource(
        XElement element,
        List<string> diagnostics,
        out ClassIslandThemeResource resource)
    {
        switch (element.Name.LocalName)
        {
            case "Color":
                if (ClassIslandThemeColor.TryParseAxaml(element.Value, out var color))
                {
                    resource = new ClassIslandThemeColorResource(color);
                    return true;
                }
                break;

            case "SolidColorBrush":
                if (ClassIslandThemeColor.TryParseAxaml(
                        element.Attribute("Color")?.Value ?? element.Value,
                        out color))
                {
                    resource = new ClassIslandThemeSolidBrushResource(
                        color,
                        ParseDouble(element.Attribute("Opacity")?.Value, 1));
                    return true;
                }
                break;

            case "LinearGradientBrush":
                resource = new ClassIslandThemeLinearGradientResource(
                    ParsePoint(element.Attribute("StartPoint")?.Value, new(0, 0)),
                    ParsePoint(element.Attribute("EndPoint")?.Value, new(1, 1)),
                    ParseGradientStops(element, diagnostics));
                return true;

            case "RadialGradientBrush":
                resource = new ClassIslandThemeRadialGradientResource(
                    ParsePoint(element.Attribute("Center")?.Value, new(0.5, 0.5)),
                    ParsePoint(element.Attribute("GradientOrigin")?.Value, new(0.5, 0.5)),
                    ParseLength(element.Attribute("RadiusX")?.Value, 0.5),
                    ParseLength(element.Attribute("RadiusY")?.Value, 0.5),
                    ParseGradientStops(element, diagnostics));
                return true;

            case "ConicGradientBrush":
                diagnostics.Add("ConicGradientBrush 将在 WPF 中用线性渐变近似，原始渐变停止点会保留。");
                resource = new ClassIslandThemeConicGradientResource(
                    ParsePoint(element.Attribute("Center")?.Value, new(0.5, 0.5)),
                    ParseDouble(element.Attribute("Angle")?.Value, 0),
                    ParseGradientStops(element, diagnostics));
                return true;

            case "DrawingBrush":
                var layers = new List<ClassIslandThemeDrawingLayer>();
                foreach (var drawing in element.Descendants().Where(x => x.Name.LocalName == "GeometryDrawing"))
                {
                    var geometryElement = drawing
                        .Elements()
                        .FirstOrDefault(x => x.Name.LocalName == "GeometryDrawing.Geometry")
                        ?.Elements()
                        .FirstOrDefault();
                    var brushElement = drawing
                        .Elements()
                        .FirstOrDefault(x => x.Name.LocalName == "GeometryDrawing.Brush")
                        ?.Elements()
                        .FirstOrDefault();
                    if (geometryElement is null || brushElement is null ||
                        !TryParseResource(brushElement, diagnostics, out var layerBrush))
                        continue;

                    var geometryData = geometryElement.Attribute("Rect")?.Value ??
                                       geometryElement.Attribute("Data")?.Value ??
                                       geometryElement.Value;
                    layers.Add(new(
                        geometryElement.Name.LocalName,
                        geometryData,
                        layerBrush));
                }
                resource = new ClassIslandThemeDrawingBrushResource(layers);
                return layers.Count > 0;

            default:
                var raw = element.Attribute("Value")?.Value;
                if (raw is null && !element.HasElements)
                    raw = element.Value;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    resource = new ClassIslandThemeLiteralResource(element.Name.LocalName, raw.Trim());
                    return true;
                }
                break;
        }

        resource = new ClassIslandThemeLiteralResource(element.Name.LocalName, element.Value.Trim());
        diagnostics.Add($"无法完整解析资源类型 {element.Name.LocalName}，已保留字面值。");
        return !string.IsNullOrWhiteSpace(element.Value);
    }

    private static IReadOnlyList<ClassIslandThemeGradientStop> ParseGradientStops(
        XElement brush,
        List<string> diagnostics)
    {
        var stops = new List<ClassIslandThemeGradientStop>();
        foreach (var stop in brush.Descendants().Where(x => x.Name.LocalName == "GradientStop"))
        {
            if (!ClassIslandThemeColor.TryParseAxaml(stop.Attribute("Color")?.Value, out var color))
            {
                diagnostics.Add($"忽略无法解析的 GradientStop 颜色：{stop.Attribute("Color")?.Value}");
                continue;
            }

            stops.Add(new(
                Math.Clamp(ParseDouble(stop.Attribute("Offset")?.Value, 0), 0, 1),
                color));
        }
        return stops;
    }

    private static ClassIslandThemeVariant ParseVariant(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "light" => ClassIslandThemeVariant.Light,
            "dark" => ClassIslandThemeVariant.Dark,
            _ => ClassIslandThemeVariant.Default
        };

    private static string? ReadKey(XElement element) =>
        element.Attributes().FirstOrDefault(x => x.Name.LocalName == "Key")?.Value;

    private static ClassIslandThemePoint ParsePoint(string? raw, ClassIslandThemePoint fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2
            ? new(ParseLength(parts[0], fallback.X), ParseLength(parts[1], fallback.Y))
            : fallback;
    }

    private static double ParseLength(string? raw, double fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        var value = raw.Trim();
        if (value.EndsWith('%') &&
            double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            return percent / 100d;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : fallback;
    }

    private static double ParseDouble(string? raw, double fallback) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static string SafeReadText(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaximumThemeTextBytes)
            throw new InvalidDataException($"主题文本过大：{path}");
        return File.ReadAllText(path);
    }

    private static string? SafeReadRelativeDirectoryFile(string root, string relative)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
            return null;
        return SafeReadText(candidate);
    }

    private static string SafeReadEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > MaximumThemeTextBytes)
            throw new InvalidDataException($"主题包文本过大：{entry.FullName}");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? SafeReadRelativeArchiveEntry(ZipArchive archive, string relative)
    {
        var normalized = NormalizeRelativePath(relative);
        var entry = archive.Entries.FirstOrDefault(x =>
            NormalizeRelativePath(x.FullName).Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return entry is null ? null : SafeReadEntry(entry);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string name)
    {
        var normalized = NormalizeRelativePath(name);
        return archive.Entries.FirstOrDefault(x =>
            NormalizeRelativePath(x.FullName).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveRelativePath(string currentPath, string include)
    {
        var virtualRoot = Path.Combine(Path.GetTempPath(), "exusiai-theme-root");
        var rootWithSeparator = Path.GetFullPath(virtualRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var currentDirectory = Path.GetDirectoryName(currentPath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        var candidate = Path.GetFullPath(Path.Combine(
            virtualRoot,
            currentDirectory,
            include.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"主题包含路径越界：{include}");
        return Path.GetRelativePath(virtualRoot, candidate).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}
