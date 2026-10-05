using System.Text.Json;
using Xunit;

namespace KTPFileDistributor.Tests;

public class TrackedConfigTests
{
    private static readonly string[] CodeExtensions = { ".amxx", ".so", ".dll", ".dylib", ".exe" };

    private static string[] TrackedWatchPatterns()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KTPFileDistributor", "appsettings.json")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(dir!.FullName, "KTPFileDistributor", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        return doc.RootElement.GetProperty("AppSettings").GetProperty("WatchPatterns")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    [Fact]
    public void TrackedWatchPatternsNameNoPluginOrModuleExtension()
    {
        var patterns = TrackedWatchPatterns();

        Assert.NotEmpty(patterns);
        var offenders = patterns.Where(p => CodeExtensions.Any(x => p.EndsWith(x, StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.True(offenders.Count == 0, "plugins ship via stage-wave.py, not the distributor: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TrackedWatchPatternsAreNotACatchAll()
    {
        Assert.DoesNotContain("*.*", TrackedWatchPatterns());
    }
}
