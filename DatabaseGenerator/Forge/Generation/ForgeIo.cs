#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DatabaseGenerator.Forge.Generation;

internal static class ForgeIo
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private const string LakeMarkerName = ".contoso-forge-lake";
    private const string LakeMarkerContent = "contoso-forge-lake-v1";

    public static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n").TrimEnd() + "\n", Utf8WithoutBom);
    }

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string Sha256Text(string value) =>
        Convert.ToHexString(SHA256.HashData(Utf8WithoutBom.GetBytes(value))).ToLowerInvariant();

    public static string DatasetFingerprint(IEnumerable<KeyValuePair<string, string>> hashes)
    {
        var canonical = string.Join("\n", hashes
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}:{item.Value}"));
        return Sha256Text(canonical);
    }

    public static void CopyTreeWithTokens(
        string sourceRoot,
        string destinationRoot,
        IReadOnlyDictionary<string, string> tokens)
    {
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Forge template directory was not found: {sourceRoot}");

        foreach (var sourcePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                     .Where(path => !Path.GetRelativePath(sourceRoot, path)
                         .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         .Any(segment => segment is "target" or "logs" or "dbt_packages" or "__pycache__"))
                     .Where(path => !path.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(sourceRoot, sourcePath);
            var destinationPath = Path.Combine(destinationRoot, relative);
            var text = File.ReadAllText(sourcePath);
            foreach (var token in tokens.OrderBy(item => item.Key, StringComparer.Ordinal))
                text = text.Replace(token.Key, token.Value, StringComparison.Ordinal);
            WriteText(destinationPath, text);
        }
    }

    public static void MaterializeRaw(string sourceRoot, string lakeRoot)
    {
        var absoluteLakeRoot = Path.GetFullPath(lakeRoot);
        ValidateLakeRootForMaterialization(absoluteLakeRoot);
        Directory.CreateDirectory(absoluteLakeRoot);
        var markerPath = Path.Combine(absoluteLakeRoot, LakeMarkerName);
        WriteText(markerPath, LakeMarkerContent);
        var rawRoot = Path.Combine(absoluteLakeRoot, "raw");
        if (Directory.Exists(rawRoot))
            Directory.Delete(rawRoot, recursive: true);
        Directory.CreateDirectory(rawRoot);
        File.WriteAllBytes(Path.Combine(rawRoot, ".gitkeep"), Array.Empty<byte>());
        foreach (var sourcePath in Directory.EnumerateFiles(sourceRoot, "*.csv", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            File.Copy(sourcePath, Path.Combine(rawRoot, Path.GetFileName(sourcePath)), true);
        }
    }

    public static void ValidateLakeRootForMaterialization(string lakeRoot)
    {
        var absoluteLakeRoot = Path.GetFullPath(lakeRoot);
        var hasEntries = Directory.Exists(absoluteLakeRoot) &&
            Directory.EnumerateFileSystemEntries(absoluteLakeRoot).Any();
        var markerPath = Path.Combine(absoluteLakeRoot, LakeMarkerName);
        var hasValidMarker = File.Exists(markerPath) &&
            string.Equals(File.ReadAllText(markerPath).Trim(), LakeMarkerContent, StringComparison.Ordinal);
        if (hasEntries && !hasValidMarker)
            throw new InvalidOperationException(
                $"Refusing to reset lake/raw beneath a non-empty lake directory without a valid Forge ownership marker: {absoluteLakeRoot}");
    }
}
