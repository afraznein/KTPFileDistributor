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

/// <summary>
/// The FastDL entry publishes to a public docroot, so its filter must name what MAY be
/// published rather than what may not. A deny-list only gates the extensions somebody
/// thought of, so the next WatchPatterns addition publishes itself -- which is what
/// `*.tga` did on 2026-09-13.
/// </summary>
public class FastDlAllowListTests
{
    private static ServerConfig ShippedFastDlEntry()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KTPFileDistributor", "servers.example.json")))
            dir = dir.Parent;

        // Not an Assert: a test that silently degrades to "found nothing, nothing to check"
        // is the failure this class exists to catch.
        if (dir is null)
            throw new InvalidOperationException("KTPFileDistributor/servers.example.json not found above " + AppContext.BaseDirectory);

        var servers = System.Text.Json.JsonSerializer.Deserialize<List<ServerConfig>>(
            File.ReadAllText(Path.Combine(dir.FullName, "KTPFileDistributor", "servers.example.json")),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        return servers.Single(s => s.Name.Contains("FastDL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShippedFastDlEntryGatesByAllowListNotDenyList()
    {
        var fastdl = ShippedFastDlEntry();

        Assert.NotEmpty(fastdl.IncludePatterns);
        Assert.Empty(fastdl.ExcludePatterns);
    }

    [Theory]
    // Client downloads -- the reason the target exists. One of these failing means the
    // allow-list stopped publishing something, which is its one real cost.
    [InlineData("maps/dod_anzio.bsp", true)]
    [InlineData("dod_anzio.wad", true)]
    [InlineData("models/player/gordon.mdl", true)]
    [InlineData("sprites/flare1.spr", true)]
    [InlineData("sound/ambience/rain.wav", true)]
    [InlineData("gfx/env/dod_anziort.tga", true)]
    [InlineData("overviews/dod_anzio.bmp", true)]
    [InlineData("overviews/dod_anzio.txt", true)]
    [InlineData("maps/dod_anzio.res", true)]
    // Compressed FastDL assets. Clients fetch these, and dropping them from the list
    // stops downloads with no error anywhere.
    [InlineData("maps/dod_anzio.bsp.ztmp", true)]
    // Server-side only. A dodserver.cfg can carry rcon_password.
    [InlineData("dodserver.cfg", false)]
    [InlineData("addons/ktpamx/configs/discord.ini", false)]
    [InlineData("addons/ktpamx/plugins/ktp_match.amxx", false)]
    [InlineData("addons/ktpamx/modules/dodx_amxx_i386.so", false)]
    [InlineData("distributor.log", false)]
    // A backup matches no whole extension, so the allow-list refuses it -- the opposite
    // of what the deny-list did with the same path.
    [InlineData("dodserver.cfg.bak-20260913", false)]
    public void ShippedFastDlEntryPublishesAssetsAndNothingElse(string path, bool published) =>
        Assert.Equal(published, ShippedFastDlEntry().Accepts(path));

    [Fact]
    public void AnExtensionNobodyHasListedIsNotPublished()
    {
        // The whole point of the conversion, stated without naming a real extension: a
        // type added to WatchPatterns later reaches the game servers and stops here until
        // somebody decides it may be published.
        var fastdl = ShippedFastDlEntry();

        Assert.False(fastdl.Accepts("maps/whatever.zzznew"));
        // Control: the same entry still accepts something it does list, so the assertion
        // above is about the filter and not about a broken path or an empty list.
        Assert.True(fastdl.Accepts("maps/whatever.bsp"));
        // Control: an unfiltered game-server target accepts both.
        var game = new ServerConfig { Name = "game" };
        Assert.True(game.Accepts("maps/whatever.zzznew"));
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
        Assert.True(server.Skipped);
    }

    [Fact]
    public async Task SameBatchWithoutAFilterDoesTryToConnect()
    {
        // Control for the test above: without it, a service that never connects to
        // anything would pass as "filtered".
        var result = await Service(Unreachable()).DistributeAsync(ConfigOnlyBatch);

        var server = Assert.Single(result.ServerResults);
        Assert.False(server.Success);
        Assert.False(server.Skipped);
    }
}
