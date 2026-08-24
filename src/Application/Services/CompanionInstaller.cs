using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityRestartTool.Models;

namespace UnityRestartTool.Services;

internal sealed class CompanionInstaller
{
    internal const string PackageName = "com.shw.unity-restart-companion";
    internal const string ManifestReference = "file:../LocalPackages/com.shw.unity-restart-companion";
    internal const string LegacyPackageName = "com.wepie.unity-restart-companion";
    internal const string LegacyManifestReference = "file:../LocalPackages/com.wepie.unity-restart-companion";
    private const string InstallStateFileName = ".unity-restart-install.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly PackageIdentity[] ManagedPackages =
    [
        new(PackageName, ManifestReference),
        new(LegacyPackageName, LegacyManifestReference),
    ];
    private readonly string _sourcePackagePath;

    public CompanionInstaller(string? sourcePackagePath = null)
    {
        _sourcePackagePath = sourcePackagePath ?? Path.Combine(
            AppContext.BaseDirectory,
            "EditorCompanion",
            "Package");
    }

    public string SourcePackagePath => _sourcePackagePath;

    public CompanionInstallInfo Inspect(string projectPath)
    {
        CompanionInstallInfo current = InspectPackage(
            projectPath,
            ManagedPackages[0]);
        if (current.Installed || current.HasConflict)
        {
            return current;
        }

        CompanionInstallInfo legacy = InspectPackage(
            projectPath,
            ManagedPackages[1]);
        if (legacy.Installed && !legacy.HasConflict)
        {
            return legacy with { Message = "已安装旧包名，点击安装 / 升级完成迁移" };
        }
        return legacy.Installed || legacy.HasConflict
            ? legacy with { Message = $"旧包名: {legacy.Message}" }
            : current;
    }

    private static CompanionInstallInfo InspectPackage(
        string projectPath,
        PackageIdentity package)
    {
        string targetPath = GetTargetPackagePath(projectPath, package.Name);
        string manifestPath = GetManifestPath(projectPath);
        if (!Directory.Exists(targetPath) || !File.Exists(manifestPath))
        {
            return new CompanionInstallInfo(false, false, "未安装");
        }

        try
        {
            JsonObject manifest = ReadManifest(manifestPath);
            string? reference = manifest["dependencies"]?[package.Name]?.GetValue<string>();
            if (!string.Equals(reference, package.ManifestReference, StringComparison.OrdinalIgnoreCase))
            {
                return new CompanionInstallInfo(false, true, "存在冲突的包引用");
            }

            bool modified = !VerifyInstalledFiles(targetPath, out _);
            return modified
                ? new CompanionInstallInfo(true, true, "包文件已被修改")
                : new CompanionInstallInfo(true, false, "已安装");
        }
        catch (Exception exception)
        {
            return new CompanionInstallInfo(false, true, $"检查失败: {exception.Message}");
        }
    }

    public void Install(string projectPath)
    {
        ValidateSourcePackage();

        string targetPath = GetTargetPackagePath(projectPath, PackageName);
        string legacyTargetPath = GetTargetPackagePath(projectPath, LegacyPackageName);
        string manifestPath = GetManifestPath(projectPath);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("项目缺少 Packages/manifest.json。", manifestPath);
        }

        JsonObject existingManifest = ReadManifest(manifestPath);
        JsonObject existingDependencies = existingManifest["dependencies"] as JsonObject ??
            throw new InvalidDataException("manifest.json 缺少 dependencies 对象。");
        foreach (PackageIdentity package in ManagedPackages)
        {
            EnsureManifestReferenceIsManaged(existingDependencies, package);
        }

        foreach (PackageIdentity package in ManagedPackages)
        {
            EnsurePackageCanBeChanged(
                GetTargetPackagePath(projectPath, package.Name),
                "覆盖");
        }
        if (Directory.Exists(targetPath))
        {
            Directory.Delete(targetPath, true);
        }

        CopyDirectory(_sourcePackagePath, targetPath);
        WriteInstallState(targetPath);

        try
        {
            UpdateManifest(manifestPath, dependencies =>
            {
                foreach (PackageIdentity package in ManagedPackages)
                {
                    EnsureManifestReferenceIsManaged(dependencies, package);
                }

                dependencies.Remove(LegacyPackageName);
                dependencies[PackageName] = ManifestReference;
            });
        }
        catch
        {
            Directory.Delete(targetPath, true);
            throw;
        }

        if (Directory.Exists(legacyTargetPath))
        {
            Directory.Delete(legacyTargetPath, true);
        }
    }

    public void Uninstall(string projectPath)
    {
        string manifestPath = GetManifestPath(projectPath);
        if (File.Exists(manifestPath))
        {
            JsonObject manifest = ReadManifest(manifestPath);
            JsonObject dependencies = manifest["dependencies"] as JsonObject ??
                throw new InvalidDataException("manifest.json 缺少 dependencies 对象。");
            foreach (PackageIdentity package in ManagedPackages)
            {
                EnsureManifestReferenceIsManaged(dependencies, package);
            }
        }

        foreach (PackageIdentity package in ManagedPackages)
        {
            EnsurePackageCanBeChanged(
                GetTargetPackagePath(projectPath, package.Name),
                "删除");
        }

        if (File.Exists(manifestPath))
        {
            UpdateManifest(manifestPath, dependencies =>
            {
                foreach (PackageIdentity package in ManagedPackages)
                {
                    EnsureManifestReferenceIsManaged(dependencies, package);
                    dependencies.Remove(package.Name);
                }
            });
        }

        foreach (PackageIdentity package in ManagedPackages)
        {
            string targetPath = GetTargetPackagePath(projectPath, package.Name);
            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, true);
            }
        }
    }

    private static string GetTargetPackagePath(string projectPath, string packageName) =>
        Path.Combine(projectPath, "LocalPackages", packageName);

    private static string GetManifestPath(string projectPath) =>
        Path.Combine(projectPath, "Packages", "manifest.json");

    private void ValidateSourcePackage()
    {
        string packageJsonPath = Path.Combine(_sourcePackagePath, "package.json");
        if (!Directory.Exists(_sourcePackagePath) || !File.Exists(packageJsonPath))
        {
            throw new DirectoryNotFoundException($"未找到 companion 发布内容: {_sourcePackagePath}");
        }

        string? sourcePackageName = JsonNode.Parse(File.ReadAllText(packageJsonPath))?["name"]?
            .GetValue<string>();
        if (!string.Equals(sourcePackageName, PackageName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"companion package.json 的 name 必须为 {PackageName}。");
        }
    }

    private static void EnsureManifestReferenceIsManaged(
        JsonObject dependencies,
        PackageIdentity package)
    {
        if (dependencies.TryGetPropertyValue(package.Name, out JsonNode? existing) &&
            existing is not null &&
            !string.Equals(
                existing.GetValue<string>(),
                package.ManifestReference,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"manifest 已包含不同的 {package.Name} 引用，拒绝修改。");
        }
    }

    private static void EnsurePackageCanBeChanged(string targetPath, string operation)
    {
        if (Directory.Exists(targetPath) &&
            !VerifyInstalledFiles(targetPath, out string verificationError))
        {
            throw new InvalidOperationException(
                $"companion 包 {Path.GetFileName(targetPath)} 包含用户修改，" +
                $"拒绝{operation}: {verificationError}");
        }
    }

    private static JsonObject ReadManifest(string path)
    {
        JsonNode? node = JsonNode.Parse(
            File.ReadAllText(path),
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        return node as JsonObject ?? throw new InvalidDataException("manifest.json 根节点不是对象。");
    }

    private static void UpdateManifest(string manifestPath, Action<JsonObject> mutation)
    {
        JsonObject manifest = ReadManifest(manifestPath);
        JsonObject dependencies = manifest["dependencies"] as JsonObject ??
            throw new InvalidDataException("manifest.json 缺少 dependencies 对象。");
        mutation(dependencies);

        string temporaryPath = manifestPath + ".unity-restart.tmp";
        string backupPath = manifestPath + ".unity-restart.bak";
        File.Copy(manifestPath, backupPath, true);
        try
        {
            File.WriteAllText(temporaryPath, manifest.ToJsonString(JsonOptions) + Environment.NewLine);
            File.Move(temporaryPath, manifestPath, true);
        }
        catch
        {
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, manifestPath, true);
            }
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        foreach (string directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(targetPath, relative));
        }

        Directory.CreateDirectory(targetPath);
        foreach (string file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourcePath, file);
            string destination = Path.Combine(targetPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static void WriteInstallState(string targetPath)
    {
        Dictionary<string, string> hashes = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(InstallStateFileName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                path => Path.GetRelativePath(targetPath, path).Replace('\\', '/'),
                ComputeHash,
                StringComparer.OrdinalIgnoreCase);
        InstallState state = new(1, hashes);
        File.WriteAllText(
            Path.Combine(targetPath, InstallStateFileName),
            JsonSerializer.Serialize(state, JsonOptions));
    }

    private static bool VerifyInstalledFiles(string targetPath, out string error)
    {
        string statePath = Path.Combine(targetPath, InstallStateFileName);
        if (!File.Exists(statePath))
        {
            error = "缺少安装完整性记录";
            return false;
        }

        try
        {
            InstallState? state = JsonSerializer.Deserialize<InstallState>(File.ReadAllText(statePath));
            if (state?.Files is null)
            {
                error = "安装完整性记录无效";
                return false;
            }

            foreach ((string relative, string expectedHash) in state.Files)
            {
                string path = Path.Combine(targetPath, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path) || !string.Equals(ComputeHash(path), expectedHash, StringComparison.Ordinal))
                {
                    error = relative;
                    return false;
                }
            }

            HashSet<string> expected = state.Files.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            string? unexpected = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(targetPath, path).Replace('\\', '/'))
                .FirstOrDefault(relative =>
                    !relative.Equals(InstallStateFileName, StringComparison.OrdinalIgnoreCase) &&
                    !relative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
                    !expected.Contains(relative));
            if (unexpected is not null)
            {
                error = unexpected;
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string ComputeHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record PackageIdentity(string Name, string ManifestReference);

    private sealed record InstallState(int Version, Dictionary<string, string> Files);
}

internal sealed record CompanionInstallInfo(bool Installed, bool HasConflict, string Message);
