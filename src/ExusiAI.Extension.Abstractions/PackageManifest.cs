using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ExusiAI.Extension.Abstractions;

public enum PackageType { Plugin, Theme, Widget, IconPack, TemplatePack, Provider, ResourcePack }

public enum PackageState { Discovered, Validated, Loaded, Initialized, Running, Stopping, Stopped, Failed, Disabled }

public sealed record PackageDependency
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("versionRange")] public required string VersionRange { get; init; }
}

public sealed record PackageEntryPoint
{
    [JsonPropertyName("assembly")] public required string Assembly { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
}

public sealed record PackageAssets
{
    public static PackageAssets Empty { get; } = new();
    [JsonPropertyName("files")] public ImmutableArray<string> Files { get; init; } = [];
}

public sealed record PackageManifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; }
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("type")] public required PackageType Type { get; init; }
    [JsonPropertyName("name")] public required string DisplayName { get; init; }
    [JsonPropertyName("version")] public required string Version { get; init; }
    [JsonPropertyName("apiVersion")] public required string ApiVersion { get; init; }
    [JsonPropertyName("publisher")] public required string Publisher { get; init; }
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
    [JsonPropertyName("minimumHostVersion")] public required string MinimumHostVersion { get; init; }
    [JsonPropertyName("maximumHostVersion")] public string? MaximumHostVersion { get; init; }
    [JsonPropertyName("dependencies")] public ImmutableArray<PackageDependency> Dependencies { get; init; } = [];
    [JsonPropertyName("permissions")] public ImmutableArray<string> Permissions { get; init; } = [];
    [JsonPropertyName("entryPoint")] public PackageEntryPoint? EntryPoint { get; init; }
    [JsonPropertyName("assets")] public PackageAssets Assets { get; init; } = PackageAssets.Empty;
}

public sealed record ManifestValidationError(string Code, string Message, string? Field = null);

public sealed record ManifestValidationResult(bool IsValid, ImmutableArray<ManifestValidationError> Errors)
{
    public static ManifestValidationResult Valid { get; } = new(true, []);
}
