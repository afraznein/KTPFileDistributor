using System.Text;
using KTPFileDistributor.Config;
using KTPFileDistributor.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KTPFileDistributor.Tests;

/// <summary>
/// Reproduces the shape of `Configure&lt;AppSettings&gt;(config.GetSection("AppSettings"))` in
/// Program.cs. .NET's configuration binder ADDS configured list items onto a pre-populated
/// List&lt;T&gt; default rather than replacing it, so `WatchPatterns` defaulting to a non-empty
/// list makes every deployment watch everything regardless of what's configured.
/// </summary>
public class WatchPatternConfigBindingTests
{
    private static AppSettings BindAppSettings(params (string Key, string Value)[] appSettingsEntries)
    {
        var data = appSettingsEntries.ToDictionary(e => $"AppSettings:{e.Key}", e => e.Value)!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(data!).Build();

        var settings = new AppSettings();
        configuration.GetSection("AppSettings").Bind(settings);
        return settings;
    }

    // AddInMemoryCollection can express a MISSING key but not a bound-but-empty JSON array,
    // so the one test that needs that exact shape goes through the real JSON provider.
    private static AppSettings BindAppSettingsFromJson(string json)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();

        var settings = new AppSettings();
        configuration.GetSection("AppSettings").Bind(settings);
        return settings;
    }

    private static (string Key, string Value)[] WatchPatternEntries(params string[] patterns) =>
        patterns.Select((p, i) => ($"WatchPatterns:{i}", p)).ToArray();

    [Fact]
    public void ConfiguredWatchPatternsCompletelyReplaceTheDefault()
    {
        var settings = BindAppSettings(WatchPatternEntries("*.amxx", "*.cfg"));

        Assert.Equal(new[] { "*.amxx", "*.cfg" }, settings.WatchPatterns);
    }

    [Fact]
    public void TheCompiledInWildcardIsGoneOnceAnythingIsConfigured()
    {
        // This is the bug: with the old `new() { "*.*" }` default, binding two configured
        // entries onto a list that already held one item produced THREE items, with "*.*"
        // surviving at index 0 -- so the configured narrowing never took effect.
        var settings = BindAppSettings(WatchPatternEntries("*.amxx", "*.cfg"));

        Assert.DoesNotContain("*.*", settings.WatchPatterns);
        Assert.Equal(2, settings.WatchPatterns.Count);
    }

    [Fact]
    public void TheFleetsConfiguredListBindsExactlyAsWrittenNoAppendedCatchAll()
    {
        // The list the live service has logged since at least 2026-07-31 (minus the "*.*"
        // this fix removes) -- the production evidence for what must still work post-fix.
        var fleetPatterns = new[]
        {
            "*.amxx", "*.bsp", "*.txt", "*.bmp", "*.cfg",
            "*.wad", "*.res", "*.mdl", "*.spr", "*.wav", "*.ini",
        };

        var settings = BindAppSettings(WatchPatternEntries(fleetPatterns));

        Assert.Equal(fleetPatterns, settings.WatchPatterns);
    }

    [Fact]
    public void AnUnconfiguredDeploymentStillWatchesEverything()
    {
        // No "AppSettings:WatchPatterns" section at all -- e.g. a fresh install before
        // servers.json/appsettings.json is customised.
        var settings = BindAppSettings();

        Assert.Empty(settings.WatchPatterns);
        Assert.True(PatternMatcher.MatchesWatchPatterns("anything.at.all", settings.WatchPatterns));
    }

    [Fact]
    public void ExplicitEmptyArrayInRealJsonAlsoWatchesEverything()
    {
        var settings = BindAppSettingsFromJson("""{ "AppSettings": { "WatchPatterns": [] } }""");

        Assert.Empty(settings.WatchPatterns);
        Assert.True(PatternMatcher.MatchesWatchPatterns("anything.at.all", settings.WatchPatterns));
    }
}
