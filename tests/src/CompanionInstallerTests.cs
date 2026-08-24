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
        Directory.CreateDirectory(Path.Combine(source, "Editor"));
        Directory.CreateDirectory(Path.Combine(project, "Packages"));
        File.WriteAllText(Path.Combine(source, "package.json"), "{\"name\":\"com.wepie.unity-restart-companion\",\"version\":\"1.0.0\"}");
        File.WriteAllText(Path.Combine(source, "Editor", "Companion.cs"), "internal static class Companion {}\n");
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
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(project, "Packages"));
        Directory.CreateDirectory(existingPackage);
        File.WriteAllText(Path.Combine(source, "package.json"), "{\"name\":\"com.wepie.unity-restart-companion\"}");
        File.WriteAllText(Path.Combine(existingPackage, "user-file.txt"), "preserve me");
        File.WriteAllText(
            Path.Combine(project, "Packages", "manifest.json"),
            "{\"dependencies\":{\"com.wepie.unity-restart-companion\":\"https://example.invalid/package.git\"}}");
        CompanionInstaller installer = new(source);

        Assert.Throws<InvalidOperationException>(() => installer.Install(project));
        Assert.Equal("preserve me", File.ReadAllText(Path.Combine(existingPackage, "user-file.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
