using KTPFileDistributor.Config;
using KTPFileDistributor.Models;
using KTPFileDistributor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KTPFileDistributor.Tests;

/// <summary>
/// A server whose per-server filter excludes every file in a batch never connects
/// (SftpDistributorService.UploadToServerAsync returns early), and that early return
/// used to set only Success = true -- identical to a real, instant upload. These tests
/// cover the ServerUploadResult.Skipped flag that distinguishes the two.
/// </summary>
public class SkippedServerResultModelTests
{
    private static FileChangeEvent Upload(string path, long size) =>
        new() { RelativePath = path, FullPath = "/watch/" + path, ChangeType = WatcherChangeTypes.Created, FileSize = size };

    [Fact]
    public void SkippedServerCountsAsSuccessfulButNotUploaded()
    {
        var result = new DistributionResult
        {
            Files = new List<FileChangeEvent> { Upload("dodserver.cfg", 1000) },
            ServerResults = new List<ServerUploadResult>
            {
                new() { ServerName = "game-1", Success = true, Duration = TimeSpan.FromSeconds(1.2) },
                new() { ServerName = "fastdl", Success = true, Skipped = true },
            },
        };

        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.UploadedCount);
        Assert.True(result.AllSuccessful);
        // Before this fix TotalBytesTransferred used SuccessCount, so a skip counted as
        // a second delivery of bytes it never received.
        Assert.Equal(1000, result.TotalBytesTransferred);
    }

    [Fact]
    public void SummaryNamesTheSkipSeparatelyFromSuccessCount()
    {
        var now = DateTime.UtcNow;
        var result = new DistributionResult
        {
            StartedAt = now,
            CompletedAt = now,
            Files = new List<FileChangeEvent> { Upload("dodserver.cfg", 1) },
            ServerResults = new List<ServerUploadResult>
            {
                new() { ServerName = "game-1", Success = true },
                new() { ServerName = "fastdl", Success = true, Skipped = true },
            },
        };

        Assert.Equal("1 file(s) [dodserver.cfg] -> 2/2 servers, 1 skipped in 0.0s", result.GetSummary());
    }

    [Fact]
    public void NoSkipsLeavesTheSummaryInThePre1_2_2Shape()
    {
        var now = DateTime.UtcNow;
        var result = new DistributionResult
        {
            StartedAt = now,
            CompletedAt = now,
            Files = new List<FileChangeEvent> { Upload("a.wad", 1) },
            ServerResults = new List<ServerUploadResult> { new() { ServerName = "game-1", Success = true } },
        };

        Assert.Equal("1 file(s) [a.wad] -> 1/1 servers in 0.0s", result.GetSummary());
    }
}

/// <summary>
/// Covers the Discord embed rendering. BuildResultEmbed is internal (see
/// InternalsVisibleTo in KTPFileDistributor.csproj) purely so it's directly testable
/// without a live relay.
/// </summary>
public class SkippedServerEmbedTests
{
    private static DiscordNotificationService Service() =>
        new(NullLogger<DiscordNotificationService>.Instance, Options.Create(new DiscordSettings()), new HttpClient());

    private static string FieldValue(object embed, string fieldName)
    {
        var fields = (object[])embed.GetType().GetProperty("fields")!.GetValue(embed)!;
        foreach (var f in fields)
        {
            var name = (string)f.GetType().GetProperty("name")!.GetValue(f)!;
            if (name == fieldName)
                return (string)f.GetType().GetProperty("value")!.GetValue(f)!;
        }
        throw new InvalidOperationException($"No field named {fieldName}");
    }

    private static DistributionResult ResultWithOneSkipOneUpload() => new()
    {
        Files = new List<FileChangeEvent>
        {
            new() { RelativePath = "dodserver.cfg", FullPath = "/watch/dodserver.cfg", ChangeType = WatcherChangeTypes.Created, FileSize = 512 },
        },
        ServerResults = new List<ServerUploadResult>
        {
            new() { ServerName = "KTP - Atlanta 1", Success = true, Duration = TimeSpan.FromSeconds(1.4) },
            new() { ServerName = "FastDL (Data Server)", Success = true, Skipped = true },
        },
    };

    // This is the test the task asks to fail against the old rendering: with the
    // pre-fix ternary (`s.Success ? "({Duration}s)" : "FAILED..."`) a skipped server
    // -- Success = true, Duration = default(TimeSpan) -- rendered as
    // "- FastDL (Data Server) (0.0s)", indistinguishable from a real instant upload.
    [Fact]
    public void SkippedServerRendersAsSkippedNotAZeroSecondSuccess()
    {
        var embed = Service().BuildResultEmbed(ResultWithOneSkipOneUpload());
        var serverDetails = FieldValue(embed, "Server Details");

        Assert.Contains("- FastDL (Data Server) skipped (filtered)", serverDetails);
        Assert.DoesNotContain("FastDL (Data Server) (0.0s)", serverDetails);
    }

    [Fact]
    public void ARealUploadStillShowsItsDuration()
    {
        var embed = Service().BuildResultEmbed(ResultWithOneSkipOneUpload());
        var serverDetails = FieldValue(embed, "Server Details");

        Assert.Contains("- KTP - Atlanta 1 (1.4s)", serverDetails);
    }

    [Fact]
    public void ServersFieldNamesTheSkipSeparately()
    {
        var embed = Service().BuildResultEmbed(ResultWithOneSkipOneUpload());

        Assert.Equal("2/2 successful (1 skipped)", FieldValue(embed, "Servers"));
    }

    [Fact]
    public void NoSkipsLeavesTheServersFieldInThePre1_2_2Shape()
    {
        var result = new DistributionResult
        {
            Files = new List<FileChangeEvent>
            {
                new() { RelativePath = "a.wad", FullPath = "/watch/a.wad", ChangeType = WatcherChangeTypes.Created, FileSize = 1 },
            },
            ServerResults = new List<ServerUploadResult>
            {
                new() { ServerName = "game-1", Success = true, Duration = TimeSpan.FromSeconds(0.3) },
            },
        };

        var embed = Service().BuildResultEmbed(result);

        Assert.Equal("1/1 successful", FieldValue(embed, "Servers"));
    }
}
