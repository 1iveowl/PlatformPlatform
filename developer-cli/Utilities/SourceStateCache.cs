using System.Security.Cryptography;
using System.Text;
using DeveloperCli.Installation;

namespace DeveloperCli.Utilities;

public static class SourceStateCache
{
    private static readonly string CacheDirectory = Path.Combine(Configuration.WorkspaceFolder, "developer-cli", "cache");

    public static bool IsUpToDate(string cacheKey)
    {
        var cachePath = GetCachePath(cacheKey);
        if (!File.Exists(cachePath)) return false;

        try
        {
            var savedState = File.ReadAllText(cachePath).Trim();
            return savedState == ComputeStateKey();
        }
        catch
        {
            return false;
        }
    }

    public static void Save(string cacheKey)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            File.WriteAllText(GetCachePath(cacheKey), ComputeStateKey());
        }
        catch
        {
            // Caching is best-effort; failures should not break the workflow
        }
    }

    private static string GetCachePath(string cacheKey)
    {
        return Path.Combine(CacheDirectory, $"{cacheKey}.hash");
    }

    private static string ComputeStateKey()
    {
        var head = ProcessHelper.StartProcess("git rev-parse HEAD", Configuration.SourceCodeFolder, true, exitOnError: false).Trim();
        var stash = ProcessHelper.StartProcess("git stash create", Configuration.SourceCodeFolder, true, exitOnError: false).Trim();
        var untracked = ComputeUntrackedFilesState();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{head}:{stash}:{untracked}")));
    }

    // git stash create records tracked changes only, and format selects untracked Blazor files, so adding or editing an
    // untracked file must change the key too
    private static string ComputeUntrackedFilesState()
    {
        var untrackedFiles = ProcessHelper.StartProcess("git ls-files --others --exclude-standard", Configuration.SourceCodeFolder, true, exitOnError: false);
        return string.Join('\n', untrackedFiles
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(relativePath => new FileInfo(Path.Combine(Configuration.SourceCodeFolder, relativePath)))
            .Where(file => file.Exists)
            .Select(file => $"{file.FullName}:{file.Length}:{file.LastWriteTimeUtc.Ticks}")
        );
    }
}
