using System.Text.Json.Nodes;
using UnityRestartTool.Services;

namespace UnityRestartTool.Tests;

public sealed class CompanionInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "UnityRestartTool.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void InstallAndUninstall_UpdatesManifestAndProtectsModifiedFiles()
    {
        string source = Path.Combine(_root, "source");
        string project = Path.Combine(_root, "project");
        CreateSourcePackage(source);
        Directory.CreateDirectory(Path.Combine(project, "Packages"));
        File.WriteAllText(Path.Combine(project, "Packages", "manifest.json"), "{\"dependencies\":{\"com.unity.test-framework\":\"1.1.0\"}}");
        CompanionInstaller installer = new(source);

        installer.Install(project);

        CompanionInstallInfo installed = installer.Inspect(project);
        Assert.True(installed.Installed);
        JsonObject manifest = JsonNode.Parse(
            File.ReadAllText(Path.Combine(project, "Packages", "manifest.json")))!.AsObject();
        Assert.Equal(
            CompanionInstaller.ManifestReference,
            manifest["dependencies"]![CompanionInstaller.PackageName]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(
            project,
            "Packages",
            "manifest.json.unity-restart.bak")));

        File.WriteAllText(Path.Combine(
            project,
            "LocalPackages",
            CompanionInstaller.PackageName,
            "package.json.meta"), "Unity-generated metadata");
        Assert.True(installer.Inspect(project).Installed);

        string installedCode = Path.Combine(
            project,
            "LocalPackages",
            CompanionInstaller.PackageName,
            "Editor",
            "Companion.cs");
        File.AppendAllText(installedCode, "// local change\n");
        Assert.Throws<InvalidOperationException>(() => installer.Uninstall(project));

        File.WriteAllText(installedCode, "internal static class Companion {}\n");
        installer.Uninstall(project);

        Assert.False(Directory.Exists(Path.Combine(
            project,
            "LocalPackages",
            CompanionInstaller.PackageName)));
        JsonObject afterRemoval = JsonNode.Parse(
            File.ReadAllText(Path.Combine(project, "Packages", "manifest.json")))!.AsObject();
        Assert.Null(afterRemoval["dependencies"]![CompanionInstaller.PackageName]);
        Assert.Equal("1.1.0", afterRemoval["dependencies"]!["com.unity.test-framework"]!.GetValue<string>());
    }

    [Fact]
    public void Install_WithConflictingReference_PreservesExistingPackageDirectory()
    {
        string source = Path.Combine(_root, "conflict-source");
        string project = Path.Combine(_root, "conflict-project");
        string existingPackage = Path.Combine(
            project,
            "LocalPackages",
            CompanionInstaller.PackageName);
        CreateSourcePackage(source);
        Directory.CreateDirectory(Path.Combine(project, "Packages"));
        Directory.CreateDirectory(existingPackage);
        File.WriteAllText(Path.Combine(existingPackage, "user-file.txt"), "preserve me");
        File.WriteAllText(
            Path.Combine(project, "Packages", "manifest.json"),
            "{\"dependencies\":{\"com.shw.unity-restart-companion\":\"https://example.invalid/package.git\"}}");
        CompanionInstaller installer = new(source);

        Assert.Throws<InvalidOperationException>(() => installer.Install(project));
        Assert.Equal("preserve me", File.ReadAllText(Path.Combine(existingPackage, "user-file.txt")));
    }

    [Fact]
    public void Install_WithReusablePackageButMissingReference_OnlyUpdatesManifest()
    {
        string source = Path.Combine(_root, "shared-source");
        string project = Path.Combine(_root, "shared-project");
        string manifestPath = Path.Combine(project, "Packages", "manifest.json");
        CreateSourcePackage(source);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, "{\"dependencies\":{}}");
        CompanionInstaller installer = new(source);
        installer.Install(project);

        string packagePath = Path.Combine(
            project,
            "LocalPackages",
            CompanionInstaller.PackageName);
        string generatedMetadataPath = Path.Combine(packagePath, "package.json.meta");
        File.WriteAllText(generatedMetadataPath, "preserve shared metadata");
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["dependencies"]!.AsObject().Remove(CompanionInstaller.PackageName);
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        CompanionInstallInfo beforeInstall = installer.Inspect(project);
        Assert.False(beforeInstall.Installed);
        Assert.False(beforeInstall.HasConflict);

        string blockedTemporaryPath = manifestPath + ".unity-restart.tmp";
        Directory.CreateDirectory(blockedTemporaryPath);
        Assert.Throws<UnauthorizedAccessException>(() => installer.Install(project));
        Assert.Equal("preserve shared metadata", File.ReadAllText(generatedMetadataPath));

        Directory.Delete(blockedTemporaryPath);
        installer.Install(project);

        Assert.Equal("preserve shared metadata", File.ReadAllText(generatedMetadataPath));
        Assert.True(installer.Inspect(project).Installed);
        JsonObject updatedManifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        Assert.Equal(
            CompanionInstaller.ManifestReference,
            updatedManifest["dependencies"]![CompanionInstaller.PackageName]!.GetValue<string>());
    }

    [Fact]
    public void PackageMetadata_UsesShwNamespace()
    {
        CompanionInstaller installer = new();
        JsonObject package = JsonNode.Parse(File.ReadAllText(Path.Combine(
            installer.SourcePackagePath,
            "package.json")))!.AsObject();

        Assert.Equal("com.shw.unity-restart-companion", CompanionInstaller.PackageName);
        Assert.Equal(CompanionInstaller.PackageName, package["name"]!.GetValue<string>());
        Assert.Equal("1.0.2", package["version"]!.GetValue<string>());
        Assert.Equal(
            $"file:../LocalPackages/{CompanionInstaller.PackageName}",
            CompanionInstaller.ManifestReference);
    }

    [Fact]
    public void Install_WithLegacyPackage_MigratesToShwNamespace()
    {
        string source = Path.Combine(_root, "migration-source");
        string project = Path.Combine(_root, "migration-project");
        CreateSourcePackage(source);
        Directory.CreateDirectory(Path.Combine(project, "Packages"));
        string manifestPath = Path.Combine(project, "Packages", "manifest.json");
        File.WriteAllText(manifestPath, "{\"dependencies\":{}}");
        CompanionInstaller installer = new(source);
        installer.Install(project);

        string currentPath = Path.Combine(
            project,
            "LocalPackages",
            CompanionInstaller.PackageName);
        string legacyPath = Path.Combine(
            project,
            "LocalPackages",
            CompanionInstaller.LegacyPackageName);
        Directory.Move(currentPath, legacyPath);
        JsonObject legacyManifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        JsonObject legacyDependencies = legacyManifest["dependencies"]!.AsObject();
        legacyDependencies.Remove(CompanionInstaller.PackageName);
        legacyDependencies[CompanionInstaller.LegacyPackageName] =
            CompanionInstaller.LegacyManifestReference;
        File.WriteAllText(manifestPath, legacyManifest.ToJsonString());

        CompanionInstallInfo legacyInstall = installer.Inspect(project);
        Assert.True(legacyInstall.Installed);
        Assert.Contains("旧包名", legacyInstall.Message);

        installer.Install(project);

        Assert.False(Directory.Exists(legacyPath));
        Assert.True(Directory.Exists(currentPath));
        JsonObject migratedManifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        JsonObject migratedDependencies = migratedManifest["dependencies"]!.AsObject();
        Assert.Null(migratedDependencies[CompanionInstaller.LegacyPackageName]);
        Assert.Equal(
            CompanionInstaller.ManifestReference,
            migratedDependencies[CompanionInstaller.PackageName]!.GetValue<string>());
    }

    private static void CreateSourcePackage(string source)
    {
        Directory.CreateDirectory(Path.Combine(source, "Editor"));
        JsonObject metadata = new()
        {
            ["name"] = CompanionInstaller.PackageName,
            ["version"] = "1.0.0",
        };
        File.WriteAllText(Path.Combine(source, "package.json"), metadata.ToJsonString());
        File.WriteAllText(
            Path.Combine(source, "Editor", "Companion.cs"),
            "internal static class Companion {}\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
