using KTPFileDistributor.Services;
using Xunit;

namespace KTPFileDistributor.Tests;

/// <summary>
/// The three shapes named as silently-matches-nothing traps, plus the legitimate shapes
/// that must never be flagged.
/// </summary>
public class UnsupportedWildcardShapeTests
{
    [Theory]
    [InlineData("maps/*")]
    [InlineData("**/*.cfg")]
    [InlineData("*cfg")]
    [InlineData("sound/*.wav")]
    [InlineData("*")]
    [InlineData("*.cfg*")]
    [InlineData("*.c*g")]
    [InlineData("*.*.bak")]
    public void FlagsShapesTheMatcherCannotHonour(string pattern) =>
        Assert.True(PatternMatcher.HasUnsupportedWildcardShape(pattern));

    [Theory]
    [InlineData("*.cfg")]
    [InlineData("*.*")]
    [InlineData("*.tar.gz")]
    [InlineData("server.cfg")]
    [InlineData("overviews/dodserver.cfg")]
    [InlineData("addons/ktpamx/configs/plugins.ini")]
    public void DoesNotFlagShapesTheMatcherActuallyImplements(string pattern) =>
        Assert.False(PatternMatcher.HasUnsupportedWildcardShape(pattern));

    [Fact]
    public void FindUnsupportedShapesReturnsOnlyTheBadOnesAndIsNullSafe()
    {
        Assert.Empty(PatternMatcher.FindUnsupportedShapes(null));
        Assert.Empty(PatternMatcher.FindUnsupportedShapes(Array.Empty<string>()));

        var found = PatternMatcher.FindUnsupportedShapes(new[] { "*.cfg", "maps/*", "*.ini", "*cfg" });

        Assert.Equal(new[] { "maps/*", "*cfg" }, found);
    }

    [Fact]
    public void FindUnsupportedShapesToleratesANullElementWithoutThrowing()
    {
        // An explicit `null` inside a JSON string array ("excludePatterns": ["*.cfg", null])
        // deserializes into the List<string> as a null element, not a missing one. This runs
        // unconditionally at startup (unlike MatchesAny's per-file-event use), so a crash here
        // takes the whole service down rather than failing to match one file.
        var withNull = new List<string?> { "*.cfg", null, "maps/*" };

        var found = PatternMatcher.FindUnsupportedShapes(withNull!);

        Assert.Equal(new[] { "maps/*" }, found);
    }
}

public class DescribeForLogTests
{
    [Fact]
    public void NullListUsesTheEmptyLabel() =>
        Assert.Equal("(all)", PatternMatcher.DescribeForLog(null, "(all)"));

    [Fact]
    public void EmptyListUsesTheEmptyLabel() =>
        Assert.Equal("(none)", PatternMatcher.DescribeForLog(new List<string>(), "(none)"));

    [Fact]
    public void PopulatedListIsCommaJoined() =>
        Assert.Equal("*.cfg, *.ini", PatternMatcher.DescribeForLog(new List<string> { "*.cfg", "*.ini" }, "(none)"));

    [Fact]
    public void ANullElementDoesNotThrow()
    {
        var withNull = new List<string?> { "*.cfg", null };

        Assert.Equal("*.cfg, ", PatternMatcher.DescribeForLog(withNull!, "(none)"));
    }
}
