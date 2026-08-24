using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityRestartTool.Models;

namespace UnityRestartTool.Services;

internal sealed class CompanionInstaller
{
    internal const string PackageName = "com.wepie.unity-restart-companion";
    internal const string ManifestReference = "file:../LocalPackages/com.wepie.unity-restart-companion";
    private const string InstallStateFileName = ".unity-restart-install.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
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
        string targetPath = GetTargetPackagePath(projectPath);
        string manifestPath = GetManifestPath(projectPath);
        if (!Directory.Exists(targetPath) || !File.Exists(manifestPath))
        {
            return new CompanionInstallInfo(false, false, "未安装");
        }

        try
        {
            JsonObject manifest = ReadManifest(manifestPath);
            string? reference = manifest["dependencies"]?[PackageName]?.GetValue<string>();
            if (!string.Equals(reference, ManifestReference, StringComparison.OrdinalIgnoreCase))
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
        if (!Directory.Exists(_sourcePackagePath) ||
            !File.Exists(Path.Combine(_sourcePackagePath, "package.json")))
        {
            throw new DirectoryNotFoundException($"未找到 companion 发布内容: {_sourcePackagePath}");
        }

        string targetPath = GetTargetPackagePath(projectPath);
        string manifestPath = GetManifestPath(projectPath);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("项目缺少 Packages/manifest.json。", manifestPath);
        }

        JsonObject existingManifest = ReadManifest(manifestPath);
        JsonObject existingDependencies = existingManifest["dependencies"] as JsonObject ??
            throw new InvalidDataException("manifest.json 缺少 dependencies 对象。");
        if (existingDependencies.TryGetPropertyValue(PackageName, out JsonNode? existingReference) &&
            existingReference is not null &&
            !string.Equals(
                existingReference.GetValue<string>(),
                ManifestReference,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"manifest 已包含不同的 {PackageName} 引用，拒绝覆盖。");
        }

        if (Directory.Exists(targetPath))
        {
            if (!VerifyInstalledFiles(targetPath, out string verificationError))
            {
                throw new InvalidOperationException(
                    $"companion 包包含用户修改，拒绝覆盖: {verificationError}");
            }

            Directory.Delete(targetPath, true);
        }

        CopyDirectory(_sourcePackagePath, targetPath);
        WriteInstallState(targetPath);

        try
        {
            UpdateManifest(manifestPath, dependencies =>
            {
                if (dependencies.TryGetPropertyValue(PackageName, out JsonNode? existing) &&
                    existing is not null &&
                    !string.Equals(existing.GetValue<string>(), ManifestReference, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"manifest 已包含不同的 {PackageName} 引用，拒绝覆盖。");
                }

                dependencies[PackageName] = ManifestReference;
            });
        }
        catch
        {
            Directory.Delete(targetPath, true);
            throw;
        }
    }

    public void Uninstall(string projectPath)
    {
        string targetPath = GetTargetPackagePath(projectPath);
        string manifestPath = GetManifestPath(projectPath);
        if (Directory.Exists(targetPath) && !VerifyInstalledFiles(targetPath, out string verificationError))
        {
            throw new InvalidOperationException(
                $"companion 包包含用户修改，拒绝删除: {verificationError}");
        }

        if (File.Exists(manifestPath))
        {
            UpdateManifest(manifestPath, dependencies =>
            {
                if (dependencies.TryGetPropertyValue(PackageName, out JsonNode? existing) &&
                    existing is not null &&
                    string.Equals(existing.GetValue<string>(), ManifestReference, StringComparison.OrdinalIgnoreCase))
                {
                    dependencies.Remove(PackageName);
                }
            });
        }

        if (Directory.Exists(targetPath))
        {
            Directory.Delete(targetPath, true);
        }
    }

    private static string GetTargetPackagePath(string projectPath) =>
        Path.Combine(projectPath, "LocalPackages", PackageName);

    private static string GetManifestPath(string projectPath) =>
        Path.Combine(projectPath, "Packages", "manifest.json");

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

    private sealed record InstallState(int Version, Dictionary<string, string> Files);
}

internal sealed record CompanionInstallInfo(bool Installed, bool HasConflict, string Message);
