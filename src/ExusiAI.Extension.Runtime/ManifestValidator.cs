using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Extension.Runtime;

public sealed partial class ManifestValidator
{
    public const int SupportedSchemaVersion = 1;
    public static readonly SemanticVersion CurrentHostVersion = new(0, 2, 0, "preview.4");
    public static readonly ExtensionApiVersion CurrentApiVersion = new(1);

    [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex("^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PermissionPattern();

    public ManifestValidationResult Validate(PackageManifest manifest, string packageRoot)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        var errors = ImmutableArray.CreateBuilder<ManifestValidationError>();

        if (manifest.SchemaVersion != SupportedSchemaVersion)
            Add("unsupported-schema", "Only manifest schema version 1 is supported.", "schemaVersion");
        if (string.IsNullOrWhiteSpace(manifest.Id) || !PackageIdPattern().IsMatch(manifest.Id))
            Add("invalid-id", "Package id must be lowercase, stable, and namespace-like.", "id");
        if (string.IsNullOrWhiteSpace(manifest.DisplayName)) Add("missing-name", "Display name is required.", "name");
        if (string.IsNullOrWhiteSpace(manifest.Publisher)) Add("missing-publisher", "Publisher is required.", "publisher");

        if (!SemanticVersion.TryParse(manifest.Version, out _))
            Add("invalid-version", "Package version must be a valid semantic version.", "version");
        if (!ExtensionApiVersion.TryParse(manifest.ApiVersion, out var apiVersion) || apiVersion != CurrentApiVersion)
            Add("unsupported-api", "The extension API version is not supported.", "apiVersion");

        var minimumValid = SemanticVersion.TryParse(manifest.MinimumHostVersion, out var minimum);
        if (!minimumValid)
            Add("invalid-minimum-host-version", "Minimum host version must be a semantic version.", "minimumHostVersion");
        else if (CurrentHostVersion.CompareTo(minimum) < 0)
            Add("host-incompatible", "The package requires a newer host.", "minimumHostVersion");

        if (manifest.MaximumHostVersion is not null)
        {
            if (!SemanticVersion.TryParse(manifest.MaximumHostVersion, out var maximum))
                Add("invalid-maximum-host-version", "Maximum host version must be a semantic version.", "maximumHostVersion");
            else if (CurrentHostVersion.CompareTo(maximum) > 0)
                Add("host-incompatible", "The package does not support this host version.", "maximumHostVersion");
            else if (minimumValid && minimum.CompareTo(maximum) > 0)
                Add("invalid-host-range", "Minimum host version cannot exceed maximum host version.", "maximumHostVersion");
        }

        if (manifest.Type == PackageType.Plugin && manifest.EntryPoint is null)
            Add("missing-entry-point", "Plugin packages require an entry point.", "entryPoint");
        if (manifest.EntryPoint is not null)
        {
            ValidatePath(manifest.EntryPoint.Assembly, "entry-assembly", "entryPoint.assembly");
            if (string.IsNullOrWhiteSpace(manifest.EntryPoint.Type))
                Add("missing-entry-type", "Plugin entry point type is required.", "entryPoint.type");
        }

        foreach (var dependency in manifest.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.Id) || !PackageIdPattern().IsMatch(dependency.Id))
                Add("invalid-dependency", $"Dependency id '{dependency.Id}' is invalid.", "dependencies");
            if (string.IsNullOrWhiteSpace(dependency.VersionRange))
                Add("invalid-dependency-version", $"Dependency '{dependency.Id}' needs a version range.", "dependencies");
        }

        foreach (var permission in manifest.Permissions)
            if (string.IsNullOrWhiteSpace(permission) || !PermissionPattern().IsMatch(permission))
                Add("invalid-permission", $"Permission '{permission}' is invalid.", "permissions");

        foreach (var asset in manifest.Assets.Files)
            ValidatePath(asset, "invalid-asset-path", "assets.files");

        return errors.Count == 0 ? ManifestValidationResult.Valid : new(false, errors.ToImmutable());

        void ValidatePath(string path, string code, string field)
        {
            if (!PackagePathResolver.TryResolve(packageRoot, path, out _))
                Add(code, "Package paths must be relative and remain inside the package directory.", field);
        }

        void Add(string code, string message, string field) => errors.Add(new(code, message, field));
    }
}
