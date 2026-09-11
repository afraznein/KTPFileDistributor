using KTPFileDistributor.Config;
using KTPFileDistributor.Models;
using KTPFileDistributor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KTPFileDistributor.Tests;

public class PatternMatcherTests
{
    // Verbatim copy of FileWatcherWorker.MatchesPattern before the refactor, kept as the
    // oracle: the shared matcher must agree with it on every case below.
    private static bool OriginalMatchesPattern(List<string> watchPatterns, string fileName)
    {
        if (watchPatterns.Count == 0 || watchPatterns.Contains("*.*"))
            return true;

        foreach (var pattern in watchPatterns)
        {
            if (pattern.StartsWith("*."))
            {
                var extension = pattern[1..];
                if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (pattern.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly string[][] PatternSets =
    {
        Array.Empty<string>(),
        new[] { "*.*" },
        new[] { "*.cfg" },
        new[] { "*.cfg", "*.ini" },
        new[] { "*.CFG" },
        new[] { "server.cfg" },
        new[] { "overviews/dodserver.cfg" },
        new[] { "*.amxx", "*.*" },
        new[] { "*.wad", "motd.txt" },
    };

    private static readonly string[] Paths =
    {
        "server.cfg",
        "SERVER.CFG",
        "overviews/dodserver.cfg",
        "addons/ktpamx/configs/plugins.ini",
        "maps/dod_anzio.bsp",
        "dod_anzio.wad",
        "motd.txt",
        "readme",
        "notacfg",
        "file.cfg.bak",
    };

    public static IEnumerable<object[]> Matrix() =>
        from set in PatternSets
        from path in Paths
        select new object[] { set, path };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void WatchPatternGateMatchesTheOriginalExactly(string[] patterns, string path)
    {
        var list = patterns.ToList();
        Assert.Equal(OriginalMatchesPattern(list, path), PatternMatcher.MatchesWatchPatterns(path, list));
    }

    [Theory]
    [InlineData("server.cfg", true)]
    [InlineData("overviews/dodserver.cfg", true)]
    [InlineData("addons/ktpamx/configs/plugins.INI", true)]
    [InlineData("maps/dod_anzio.bsp", false)]
    [InlineData("file.cfg.bak", false)]
    [InlineData("notacfg", false)]
    public void ExtensionPatternsMatchAtAnyDepthCaseInsensitively(string path, bool expected) =>
        Assert.Equal(expected, PatternMatcher.MatchesAny(path, new[] { "*.cfg", "*.ini" }));

    [Fact]
    public void ExactNameMustEqualTheWholeRelativePath()
    {
        Assert.True(PatternMatcher.MatchesAny("overviews/dodserver.cfg", new[] { "OVERVIEWS/DODSERVER.CFG" }));
        Assert.False(PatternMatcher.MatchesAny("overviews/dodserver.cfg", new[] { "dodserver.cfg" }));
    }

    [Fact]
    public void EmptyOrNullListMatchesNothing()
    {
        Assert.False(PatternMatcher.MatchesAny("server.cfg", Array.Empty<string>()));
        Assert.False(PatternMatcher.MatchesAny("server.cfg", null));
    }
}

public class FilesForServerTests
{
    private static FileChangeEvent Upload(string path) =>
        new() { RelativePath = path, FullPath = "/watch/" + path, ChangeType = WatcherChangeTypes.Created };

    private static FileChangeEvent Delete(string path) =>
        new() { RelativePath = path, FullPath = "/watch/" + path, ChangeType = WatcherChangeTypes.Deleted };

    private static readonly ServerConfig GameServer = new() { Name = "game" };

    private static readonly ServerConfig FastDl = new()
    {
        Name = "fastdl",
        ExcludePatterns = new() { "*.cfg", "*.ini" },
    };

    private static List<string> Paths(IEnumerable<FileChangeEvent> files) =>
        files.Select(f => f.RelativePath).ToList();

    [Fact]
    public void ExcludedFileSkipsThatServerButReachesTheOthers()
    {
        var batch = new[] { Upload("dodserver.cfg"), Upload("maps/dod_anzio.bsp") };

        Assert.Equal(new[] { "maps/dod_anzio.bsp" }, Paths(SftpDistributorService.FilesForServer(FastDl, batch)));
        Assert.Equal(new[] { "dodserver.cfg", "maps/dod_anzio.bsp" }, Paths(SftpDistributorService.FilesForServer(GameServer, batch)));
    }

    [Fact]
    public void ExcludedFileInASubdirectoryIsStillExcluded()
    {
        var batch = new[] { Upload("addons/ktpamx/configs/plugins.ini"), Upload("overviews/dod_anzio.txt") };

        Assert.Equal(new[] { "overviews/dod_anzio.txt" }, Paths(SftpDistributorService.FilesForServer(FastDl, batch)));
    }

    [Fact]
    public void ExcludedDeletionIsNotPropagatedToThatServer()
    {
        // A rename reaches the service as a Deleted event for the old name.
        var batch = new[] { Delete("probe/old-name.cfg"), Delete("sound/gone.wav") };

        var fastdl = SftpDistributorService.FilesForServer(FastDl, batch);
        Assert.Equal(new[] { "sound/gone.wav" }, Paths(fastdl));
        Assert.All(fastdl, f => Assert.Equal(WatcherChangeTypes.Deleted, f.ChangeType));

        Assert.Equal(2, SftpDistributorService.FilesForServer(GameServer, batch).Count);
    }

    [Fact]
    public void NoFilterSendsTheWholeBatchUnchanged()
    {
        var batch = new[] { Upload("a.cfg"), Delete("b.ini"), Upload("c.wad"), Upload("readme") };

        Assert.Equal(Paths(batch), Paths(SftpDistributorService.FilesForServer(GameServer, batch)));
    }

    [Fact]
    public void IncludeRestrictsAndExcludeWins()
    {
        var server = new ServerConfig
        {
            Name = "assets-only",
            IncludePatterns = new() { "*.wad", "*.spr", "*.cfg" },
            ExcludePatterns = new() { "*.cfg" },
        };
        var batch = new[] { Upload("x.wad"), Upload("sprites/y.spr"), Upload("z.cfg"), Upload("maps/m.bsp") };

        Assert.Equal(new[] { "x.wad", "sprites/y.spr" }, Paths(SftpDistributorService.FilesForServer(server, batch)));
    }

    [Fact]
    public void NullListsFromJsonBehaveAsEmpty()
    {
        var server = new ServerConfig { Name = "nulls", IncludePatterns = null!, ExcludePatterns = null! };

        Assert.True(server.Accepts("anything.cfg"));
    }

    [Fact]
    public void ServersJsonKeysBindCaseInsensitively()
    {
        const string json = """
            [{ "name": "fastdl", "host": "h", "username": "u", "excludePatterns": ["*.cfg", "*.ini"] },
             { "name": "game", "host": "h", "username": "u" }]
            """;
        var servers = System.Text.Json.JsonSerializer.Deserialize<List<ServerConfig>>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.False(servers[0].Accepts("dodserver.cfg"));
        Assert.True(servers[1].Accepts("dodserver.cfg"));
        Assert.Empty(servers[1].ExcludePatterns);
    }
}

public class DistributeAsyncFilterTests
{
    // Nothing listens on port 1, so any connection attempt fails fast. That turns
    // "did the service try to reach this server" into an observable Success flag.
    private static SftpDistributorService Service(ServerConfig server) =>
        new(
            NullLogger<SftpDistributorService>.Instance,
            Options.Create(new AppSettings
            {
                UploadRetryCount = 1,
                RetryDelayMs = 0,
                ConnectionTimeoutSeconds = 2,
                MaxConcurrentUploads = 1,
            }),
            Options.Create(new List<ServerConfig> { server }));

    private static ServerConfig Unreachable(params string[] exclude) => new()
    {
        Name = "unreachable",
        Host = "127.0.0.1",
        Port = 1,
        Username = "nobody",
        Password = "unused",
        ExcludePatterns = exclude.ToList(),
    };

    private static readonly FileChangeEvent[] ConfigOnlyBatch =
    {
        new() { RelativePath = "probe/probe.cfg", FullPath = "/nonexistent/probe/probe.cfg", ChangeType = WatcherChangeTypes.Created },
        new() { RelativePath = "probe/old.ini", FullPath = "/nonexistent/probe/old.ini", ChangeType = WatcherChangeTypes.Deleted },
    };

    [Fact]
    public async Task FullyExcludedBatchNeverConnects()
    {
        var result = await Service(Unreachable("*.cfg", "*.ini")).DistributeAsync(ConfigOnlyBatch);

        var server = Assert.Single(result.ServerResults);
        Assert.True(server.Success, server.ErrorMessage);
    }

    [Fact]
    public async Task SameBatchWithoutAFilterDoesTryToConnect()
    {
        // Control for the test above: without it, a service that never connects to
        // anything would pass as "filtered".
        var result = await Service(Unreachable()).DistributeAsync(ConfigOnlyBatch);

        var server = Assert.Single(result.ServerResults);
        Assert.False(server.Success);
    }
}
